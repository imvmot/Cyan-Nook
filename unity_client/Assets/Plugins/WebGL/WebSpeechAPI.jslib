// WebSpeechAPI.jslib
// Web Speech API wrapper for Unity WebGL

mergeInto(LibraryManager.library, {

    // グローバル変数
    _webSpeechRecognition: null,
    _recognitionCallbackObject: null,

    /**
     * Web Speech APIを初期化
     * @param {string} callbackObjectName - コールバックを受け取るGameObject名
     * @param {string} language - 認識言語（"ja-JP", "en-US"等）
     * @param {boolean} continuous - 継続的認識
     * @param {boolean} interimResults - 部分結果を取得
     */
    WebSpeech_Initialize: function(callbackObjectName, language, continuous, interimResults) {
        // SpeechRecognitionオブジェクト作成
        var SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

        if (!SpeechRecognition) {
            console.error('[WebSpeech] Speech Recognition API not supported');
            return false;
        }

        this._webSpeechRecognition = new SpeechRecognition();
        this._recognitionCallbackObject = UTF8ToString(callbackObjectName);

        // 設定
        this._webSpeechRecognition.lang = UTF8ToString(language);
        this._webSpeechRecognition.continuous = continuous;
        this._webSpeechRecognition.interimResults = interimResults;
        this._webSpeechRecognition.maxAlternatives = 1;

        // イベントハンドラ
        var callbackObj = this._recognitionCallbackObject;

        this._webSpeechRecognition.onstart = function() {
            console.log('[WebSpeech] Recognition started');
            SendMessage(callbackObj, 'OnRecognitionStarted', '');
        };

        this._webSpeechRecognition.onresult = function(event) {
            // 最新の結果を取得
            var lastIndex = event.results.length - 1;
            var result = event.results[lastIndex];
            var transcript = result[0].transcript;
            var isFinal = result.isFinal;

            console.log('[WebSpeech] Result:', transcript, 'Final:', isFinal);

            if (isFinal) {
                // 確定結果
                SendMessage(callbackObj, 'OnFinalResult', transcript);
            } else {
                // 部分結果（話している途中）
                SendMessage(callbackObj, 'OnPartialResult', transcript);
            }
        };

        this._webSpeechRecognition.onerror = function(event) {
            console.error('[WebSpeech] Error:', event.error);
            SendMessage(callbackObj, 'OnRecognitionError', event.error);
        };

        this._webSpeechRecognition.onend = function() {
            console.log('[WebSpeech] Recognition ended');
            SendMessage(callbackObj, 'OnRecognitionEnded', '');
        };

        console.log('[WebSpeech] Initialized successfully');
        return true;
    },

    /**
     * 認識開始
     */
    WebSpeech_Start: function() {
        if (!this._webSpeechRecognition) {
            console.error('[WebSpeech] Not initialized');
            return false;
        }

        try {
            this._webSpeechRecognition.start();
            return true;
        } catch (e) {
            console.error('[WebSpeech] Start failed:', e);
            return false;
        }
    },

    /**
     * 認識停止
     */
    WebSpeech_Stop: function() {
        if (!this._webSpeechRecognition) {
            console.error('[WebSpeech] Not initialized');
            return false;
        }

        try {
            this._webSpeechRecognition.stop();
            return true;
        } catch (e) {
            console.error('[WebSpeech] Stop failed:', e);
            return false;
        }
    },

    /**
     * 認識中断（即座に停止、onendイベントなし）
     */
    WebSpeech_Abort: function() {
        if (!this._webSpeechRecognition) {
            return false;
        }

        try {
            this._webSpeechRecognition.abort();
            return true;
        } catch (e) {
            console.error('[WebSpeech] Abort failed:', e);
            return false;
        }
    },

    /**
     * 言語設定変更
     */
    WebSpeech_SetLanguage: function(language) {
        if (!this._webSpeechRecognition) {
            return false;
        }

        this._webSpeechRecognition.lang = UTF8ToString(language);
        return true;
    },

    /**
     * Web Speech API対応チェック
     */
    WebSpeech_IsSupported: function() {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
    }
});
