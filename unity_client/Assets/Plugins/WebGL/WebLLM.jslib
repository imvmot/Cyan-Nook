// WebLLM.jslib
// ブラウザ内LLM推論（WebGPU）のブリッジ
// @mlc-ai/web-llm ライブラリを使用してモデルのダウンロード・推論を行う
// CDNからロードされた window.webllm を使用

mergeInto(LibraryManager.library, {

    // 内部状態（グローバル）
    $WebLLMState: {
        engine: null,
        callbackObject: '',
        isLoading: false,
        isModelLoaded: false,
        isGenerating: false,
        isLibraryLoading: false,
        isLibraryLoaded: false,
        loadedBytes: 0,
        totalBytes: 0,
        abortController: null,
        jsonSchema: null,
        // 生成パラメータ
        temperature: 0.7,
        topP: 0.9,
        maxTokens: 512,
        repeatPenalty: 1.1
    },

    // 依存宣言
    WebLLM_IsWebGPUSupported__deps: ['$WebLLMState'],
    WebLLM_Initialize__deps: ['$WebLLMState'],
    WebLLM_LoadModel__deps: ['$WebLLMState'],
    WebLLM_IsModelLoaded__deps: ['$WebLLMState'],
    WebLLM_IsLoading__deps: ['$WebLLMState'],
    WebLLM_SetGenerationParams__deps: ['$WebLLMState'],
    WebLLM_SendRequest__deps: ['$WebLLMState'],
    WebLLM_SendStreamingRequest__deps: ['$WebLLMState'],
    WebLLM_Abort__deps: ['$WebLLMState'],
    WebLLM_Unload__deps: ['$WebLLMState'],

    /**
     * WebGPUがサポートされているか確認
     * @returns {number} 1=サポート, 0=非サポート
     */
    WebLLM_IsWebGPUSupported: function() {
        return (navigator.gpu !== undefined) ? 1 : 0;
    },

    /**
     * 初期化（コールバック先GameObjectの登録 + JSONスキーマ設定）
     * @param {string} callbackObjectName - SendMessage先のGameObject名
     * @param {string} jsonSchemaStr - 構造化出力用JSONスキーマ（空文字列の場合はスキーマなし）
     */
    WebLLM_Initialize: function(callbackObjectName, jsonSchemaStr) {
        var state = WebLLMState;
        state.callbackObject = UTF8ToString(callbackObjectName);

        var schemaStr = UTF8ToString(jsonSchemaStr);
        if (schemaStr && schemaStr.length > 0) {
            try {
                state.jsonSchema = JSON.parse(schemaStr);
            } catch(e) {
                console.warn('[WebLLM] Failed to parse JSON schema:', e);
                state.jsonSchema = null;
            }
        }

        console.log('[WebLLM] Initialized, callback=' + state.callbackObject);
    },

    /**
     * モデルをダウンロード＆ロード（非同期）
     * 進捗は OnWebLLMLoadProgress で通知
     * 完了は OnWebLLMModelLoaded で通知
     * @param {string} modelIdStr - MLCモデルID（例: "Qwen3-1.7B-q4f16_1-MLC"）
     */
    WebLLM_LoadModel: function(modelIdStr) {
        var state = WebLLMState;
        var modelId = UTF8ToString(modelIdStr);

        if (state.isLoading) {
            console.warn('[WebLLM] Already loading');
            return;
        }
        if (state.isModelLoaded) {
            console.log('[WebLLM] Model already loaded');
            SendMessage(state.callbackObject, 'OnWebLLMModelLoaded', 'ok');
            return;
        }

        state.isLoading = true;
        state.loadedBytes = 0;
        state.totalBytes = 0;

        // web-llm ライブラリのロード待機（ES Moduleは非同期のためUnityより遅れる場合がある）
        function doLoadModel() {
            console.log('[WebLLM] Loading model: ' + modelId);
            window.webllm.CreateMLCEngine(modelId, {
                initProgressCallback: function(report) {
                    var estimatedTotal = 1200000000;
                    state.loadedBytes = Math.floor(report.progress * estimatedTotal);
                    state.totalBytes = estimatedTotal;

                    var progressJson = JSON.stringify({
                        loaded: state.loadedBytes,
                        total: state.totalBytes,
                        progress: report.progress,
                        text: report.text
                    });
                    SendMessage(state.callbackObject, 'OnWebLLMLoadProgress', progressJson);
                }
            }).then(function(engine) {
                state.engine = engine;
                state.isLoading = false;
                state.isModelLoaded = true;
                console.log('[WebLLM] Model loaded successfully');
                SendMessage(state.callbackObject, 'OnWebLLMModelLoaded', 'ok');
            }).catch(function(error) {
                state.isLoading = false;
                console.error('[WebLLM] Load failed:', error);
                SendMessage(state.callbackObject, 'OnWebLLMError', 'Load failed: ' + error.message);
            });
        }

        // ライブラリが既にロード済みならすぐ実行、未ロードなら動的にCDNから読み込み
        if (typeof window.webllm !== 'undefined') {
            doLoadModel();
        } else if (state.isLibraryLoading) {
            // 読み込み中なら完了イベントを待機
            window.addEventListener('webllm-ready', function() {
                doLoadModel();
            }, { once: true });
        } else {
            state.isLibraryLoading = true;
            console.log('[WebLLM] Dynamically loading web-llm library from CDN...');
            SendMessage(state.callbackObject, 'OnWebLLMLoadProgress', JSON.stringify({
                loaded: 0, total: 0, progress: 0, text: 'Loading WebLLM library...'
            }));

            // ES Module動的インポート
            var script = document.createElement('script');
            script.type = 'module';
            script.textContent =
                'import * as webllm from "https://esm.run/@mlc-ai/web-llm";' +
                'window.webllm = webllm;' +
                'window.dispatchEvent(new Event("webllm-ready"));' +
                'console.log("[WebLLM] Library loaded from CDN (dynamic)");';
            document.head.appendChild(script);

            window.addEventListener('webllm-ready', function() {
                state.isLibraryLoading = false;
                state.isLibraryLoaded = true;
                doLoadModel();
            }, { once: true });

            // ライブラリ読み込みタイムアウト（30秒）
            setTimeout(function() {
                if (!state.isLibraryLoaded && state.isLibraryLoading) {
                    state.isLibraryLoading = false;
                    state.isLoading = false;
                    console.error('[WebLLM] Library load timed out');
                    SendMessage(state.callbackObject, 'OnWebLLMError',
                        'WebLLM library failed to load. The hosting environment may not support dynamic ES Module imports.');
                }
            }, 30000);
        }
    },

    /**
     * モデルがロード済みか確認
     * @returns {number} 1=ロード済み, 0=未ロード
     */
    WebLLM_IsModelLoaded: function() {
        return WebLLMState.isModelLoaded ? 1 : 0;
    },

    /**
     * モデルロード中か確認
     * @returns {number} 1=ロード中, 0=ロードしていない
     */
    WebLLM_IsLoading: function() {
        return WebLLMState.isLoading ? 1 : 0;
    },

    /**
     * 生成パラメータを設定（リクエスト前に呼び出す）
     * @param {number} temperature - Temperature (0.0-2.0)
     * @param {number} topP - Top P (0.0-1.0)
     * @param {number} maxTokens - 最大トークン数 (-1=無制限)
     * @param {number} repeatPenalty - 繰り返しペナルティ (1.0=無効)
     */
    WebLLM_SetGenerationParams: function(temperature, topP, maxTokens, repeatPenalty) {
        var state = WebLLMState;
        state.temperature = temperature;
        state.topP = topP;
        state.maxTokens = maxTokens > 0 ? maxTokens : undefined;
        state.repeatPenalty = repeatPenalty;
    },

    /**
     * 非ストリーミングリクエスト送信（非同期）
     * 結果は OnWebLLMResponse で通知
     * @param {string} systemPromptStr - システムプロンプト
     * @param {string} userMessageStr - ユーザーメッセージ
     */
    WebLLM_SendRequest: function(systemPromptStr, userMessageStr) {
        var state = WebLLMState;
        if (!state.engine || !state.isModelLoaded) {
            SendMessage(state.callbackObject, 'OnWebLLMError', 'Model not loaded');
            return;
        }
        if (state.isGenerating) {
            // 前の生成を中断してから新しいリクエストを処理
            console.log('[WebLLM] Interrupting previous generation for new request');
            state.engine.interruptGenerate();
            state.isGenerating = false;
        }

        var systemPrompt = UTF8ToString(systemPromptStr);
        var userMessage = UTF8ToString(userMessageStr);
        state.isGenerating = true;

        // Qwen3: Thinkingモード無効化（/no_thinkをシステムプロンプト末尾に追加）
        var fullSystemPrompt = systemPrompt + '\n/no_think';

        var request = {
            messages: [
                { role: "system", content: fullSystemPrompt },
                { role: "user", content: userMessage }
            ],
            temperature: state.temperature,
            top_p: state.topP,
            frequency_penalty: state.repeatPenalty > 1.0 ? state.repeatPenalty - 1.0 : 0.0
        };
        if (state.maxTokens) request.max_tokens = state.maxTokens;

        // XGrammar 構造化出力
        if (state.jsonSchema) {
            request.response_format = {
                type: "json_object",
                schema: state.jsonSchema
            };
        }

        state.engine.chat.completions.create(request).then(function(reply) {
            state.isGenerating = false;
            var content = reply.choices[0].message.content || '';
            // <think>タグが含まれている場合は除去
            content = content.replace(/<think>[\s\S]*?<\/think>/g, '').trim();
            SendMessage(state.callbackObject, 'OnWebLLMResponse', content);
        }).catch(function(error) {
            state.isGenerating = false;
            console.error('[WebLLM] Request failed:', error);
            SendMessage(state.callbackObject, 'OnWebLLMError', 'Request failed: ' + error.message);
        });
    },

    /**
     * ストリーミングリクエスト送信（非同期）
     * チャンクは OnWebLLMStreamChunk、完了は OnWebLLMStreamComplete で通知
     * @param {string} systemPromptStr - システムプロンプト
     * @param {string} userMessageStr - ユーザーメッセージ
     */
    WebLLM_SendStreamingRequest: function(systemPromptStr, userMessageStr) {
        var state = WebLLMState;
        if (!state.engine || !state.isModelLoaded) {
            SendMessage(state.callbackObject, 'OnWebLLMError', 'Model not loaded');
            return;
        }
        if (state.isGenerating) {
            // 前の生成を中断してから新しいリクエストを処理
            console.log('[WebLLM] Interrupting previous generation for new streaming request');
            state.engine.interruptGenerate();
            state.isGenerating = false;
        }

        var systemPrompt = UTF8ToString(systemPromptStr);
        var userMessage = UTF8ToString(userMessageStr);
        state.isGenerating = true;

        // Qwen3: Thinkingモード無効化
        var fullSystemPrompt = systemPrompt + '\n/no_think';

        var request = {
            messages: [
                { role: "system", content: fullSystemPrompt },
                { role: "user", content: userMessage }
            ],
            temperature: state.temperature,
            top_p: state.topP,
            frequency_penalty: state.repeatPenalty > 1.0 ? state.repeatPenalty - 1.0 : 0.0,
            stream: true,
            stream_options: { include_usage: true }
        };
        if (state.maxTokens) request.max_tokens = state.maxTokens;

        // XGrammar 構造化出力
        if (state.jsonSchema) {
            request.response_format = {
                type: "json_object",
                schema: state.jsonSchema
            };
        }

        // async iteration over streaming response
        // <think>タグのストリーミング除去用フラグ
        (async function() {
            var insideThink = false;
            try {
                var chunks = await state.engine.chat.completions.create(request);
                for await (var chunk of chunks) {
                    if (chunk.choices && chunk.choices[0] && chunk.choices[0].delta) {
                        var content = chunk.choices[0].delta.content;
                        if (content) {
                            // <think>タグ内のテキストをストリーミング中にフィルタリング
                            if (content.indexOf('<think>') !== -1) {
                                insideThink = true;
                                content = content.replace(/<think>[\s\S]*/g, '');
                            }
                            if (insideThink) {
                                if (content.indexOf('</think>') !== -1) {
                                    insideThink = false;
                                    content = content.replace(/[\s\S]*<\/think>/g, '');
                                } else {
                                    continue; // think内のチャンクはスキップ
                                }
                            }
                            if (content) {
                                SendMessage(state.callbackObject, 'OnWebLLMStreamChunk', content);
                            }
                        }
                    }
                }
                state.isGenerating = false;
                SendMessage(state.callbackObject, 'OnWebLLMStreamComplete', '');
            } catch(error) {
                state.isGenerating = false;
                console.error('[WebLLM] Streaming failed:', error);
                SendMessage(state.callbackObject, 'OnWebLLMError', 'Streaming failed: ' + error.message);
            }
        })();
    },

    /**
     * 生成を中断
     */
    WebLLM_Abort: function() {
        var state = WebLLMState;
        if (state.engine && state.isGenerating) {
            state.engine.interruptGenerate();
            state.isGenerating = false;
            console.log('[WebLLM] Generation aborted');
        }
    },

    /**
     * モデルをアンロードしてメモリ解放
     */
    WebLLM_Unload: function() {
        var state = WebLLMState;
        if (state.engine) {
            state.engine.unload().then(function() {
                state.engine = null;
                state.isModelLoaded = false;
                state.isGenerating = false;
                console.log('[WebLLM] Model unloaded');
            });
        }
    }
});
