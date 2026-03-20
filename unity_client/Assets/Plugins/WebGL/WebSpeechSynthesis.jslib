// WebSpeechSynthesis.jslib
// Web Speech Synthesis API wrapper for Unity WebGL (TTS)

mergeInto(LibraryManager.library, {

    // グローバル変数
    _webSpeechSynthCallbackObject: null,
    _webSpeechSynthQueue: [],
    _webSpeechSynthIsSpeaking: false,
    _webSpeechSynthCurrentVoiceURI: '',
    _webSpeechSynthCurrentRate: 1.0,
    _webSpeechSynthCurrentPitch: 1.0,
    // キュー処理関数（Initialize時にクロージャとして設定）
    _webSpeechSynthProcessQueue: null,

    /**
     * Web Speech Synthesis APIを初期化
     * @param {string} callbackObjectName - コールバックを受け取るGameObject名
     */
    WebSpeechSynth_Initialize: function(callbackObjectName) {
        if (!window.speechSynthesis) {
            console.error('[WebSpeechSynth] SpeechSynthesis API not supported');
            return false;
        }

        this._webSpeechSynthCallbackObject = UTF8ToString(callbackObjectName);
        this._webSpeechSynthQueue = [];
        this._webSpeechSynthIsSpeaking = false;

        var callbackObj = this._webSpeechSynthCallbackObject;

        // キュー処理関数をクロージャとして作成・保存
        // （ライブラリ関数はthis経由で呼び出せないため、プロパティに関数を格納）
        var self = this;
        this._webSpeechSynthProcessQueue = function() {
            if (self._webSpeechSynthQueue.length === 0) {
                self._webSpeechSynthIsSpeaking = false;
                if (callbackObj) {
                    SendMessage(callbackObj, 'OnQueueEmpty', '');
                }
                return;
            }

            var textStr = self._webSpeechSynthQueue.shift();
            var voiceURIStr = self._webSpeechSynthCurrentVoiceURI;
            var rate = self._webSpeechSynthCurrentRate;
            var pitch = self._webSpeechSynthCurrentPitch;

            var utterance = new SpeechSynthesisUtterance(textStr);
            utterance.rate = rate;
            utterance.pitch = pitch;
            utterance.lang = 'ja-JP';

            // 音声を設定
            if (voiceURIStr) {
                var voices = window.speechSynthesis.getVoices();
                for (var i = 0; i < voices.length; i++) {
                    if (voices[i].voiceURI === voiceURIStr) {
                        utterance.voice = voices[i];
                        break;
                    }
                }
            }

            utterance.onstart = function() {
                self._webSpeechSynthIsSpeaking = true;
                console.log('[WebSpeechSynth] Playing from queue: ' + textStr.substring(0, 20) + '... (Remaining: ' + self._webSpeechSynthQueue.length + ')');
                if (callbackObj) {
                    SendMessage(callbackObj, 'OnSpeechStarted', '');
                }
            };

            utterance.onend = function() {
                console.log('[WebSpeechSynth] Queue item ended');
                if (callbackObj) {
                    SendMessage(callbackObj, 'OnSpeechEnded', '');
                }
                // 次のキューアイテムを処理
                self._webSpeechSynthProcessQueue();
            };

            utterance.onerror = function(event) {
                console.error('[WebSpeechSynth] Queue item error: ' + event.error);
                if (callbackObj) {
                    SendMessage(callbackObj, 'OnSpeechError', event.error || 'unknown');
                }
                // エラーでも次のキューアイテムを処理
                self._webSpeechSynthProcessQueue();
            };

            window.speechSynthesis.speak(utterance);
        };

        // 音声リスト読み込み完了イベント
        var notifyVoices = function() {
            var voices = window.speechSynthesis.getVoices();
            // 日本語のみフィルタ
            var jaVoices = [];
            for (var i = 0; i < voices.length; i++) {
                if (voices[i].lang && voices[i].lang.indexOf('ja') === 0) {
                    jaVoices.push({
                        name: voices[i].name,
                        lang: voices[i].lang,
                        voiceURI: voices[i].voiceURI,
                        isDefault: voices[i].default
                    });
                }
            }
            var json = JSON.stringify(jaVoices);
            console.log('[WebSpeechSynth] Japanese voices loaded: ' + jaVoices.length);
            SendMessage(callbackObj, 'OnVoicesLoaded', json);
        };

        // voiceschangedイベントで通知（ブラウザによっては非同期読み込み）
        window.speechSynthesis.onvoiceschanged = notifyVoices;

        // 既に読み込み済みの場合は即座に通知
        var voices = window.speechSynthesis.getVoices();
        if (voices.length > 0) {
            notifyVoices();
        }

        console.log('[WebSpeechSynth] Initialized successfully');
        return true;
    },

    /**
     * Web Speech Synthesis API対応チェック
     */
    WebSpeechSynth_IsSupported: function() {
        return !!window.speechSynthesis;
    },

    /**
     * 日本語音声リストをJSON文字列で取得
     */
    WebSpeechSynth_GetVoices: function() {
        if (!window.speechSynthesis) {
            var empty = '[]';
            var bufferSize = lengthBytesUTF8(empty) + 1;
            var buffer = _malloc(bufferSize);
            stringToUTF8(empty, buffer, bufferSize);
            return buffer;
        }

        var voices = window.speechSynthesis.getVoices();
        var jaVoices = [];
        for (var i = 0; i < voices.length; i++) {
            if (voices[i].lang && voices[i].lang.indexOf('ja') === 0) {
                jaVoices.push({
                    name: voices[i].name,
                    lang: voices[i].lang,
                    voiceURI: voices[i].voiceURI,
                    isDefault: voices[i].default
                });
            }
        }

        var json = JSON.stringify(jaVoices);
        var bufferSize = lengthBytesUTF8(json) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(json, buffer, bufferSize);
        return buffer;
    },

    /**
     * テキストを即座に発話（テスト用）
     * 現在のキューをクリアして即時発話
     */
    WebSpeechSynth_Speak: function(text, voiceURI, rate, pitch) {
        if (!window.speechSynthesis) {
            console.error('[WebSpeechSynth] Not supported');
            return;
        }

        // 現在の発話とキューをクリア
        window.speechSynthesis.cancel();
        this._webSpeechSynthQueue = [];
        this._webSpeechSynthIsSpeaking = false;

        var textStr = UTF8ToString(text);
        var voiceURIStr = UTF8ToString(voiceURI);

        var utterance = new SpeechSynthesisUtterance(textStr);
        utterance.rate = rate;
        utterance.pitch = pitch;
        utterance.lang = 'ja-JP';

        // 音声を設定
        if (voiceURIStr) {
            var voices = window.speechSynthesis.getVoices();
            for (var i = 0; i < voices.length; i++) {
                if (voices[i].voiceURI === voiceURIStr) {
                    utterance.voice = voices[i];
                    break;
                }
            }
        }

        var callbackObj = this._webSpeechSynthCallbackObject;
        var self = this;

        utterance.onstart = function() {
            self._webSpeechSynthIsSpeaking = true;
            console.log('[WebSpeechSynth] Speaking: ' + textStr.substring(0, 20) + '...');
            if (callbackObj) {
                SendMessage(callbackObj, 'OnSpeechStarted', '');
            }
        };

        utterance.onend = function() {
            self._webSpeechSynthIsSpeaking = false;
            console.log('[WebSpeechSynth] Speech ended');
            if (callbackObj) {
                SendMessage(callbackObj, 'OnSpeechEnded', '');
            }
        };

        utterance.onerror = function(event) {
            self._webSpeechSynthIsSpeaking = false;
            console.error('[WebSpeechSynth] Error: ' + event.error);
            if (callbackObj) {
                SendMessage(callbackObj, 'OnSpeechError', event.error || 'unknown');
            }
        };

        window.speechSynthesis.speak(utterance);
    },

    /**
     * テキストをキューに追加（ストリーミング用）
     * 現在発話中でなければ即座に開始、発話中ならキューに追加
     */
    WebSpeechSynth_Enqueue: function(text, voiceURI, rate, pitch) {
        if (!window.speechSynthesis) {
            console.error('[WebSpeechSynth] Not supported');
            return;
        }

        var textStr = UTF8ToString(text);
        var voiceURIStr = UTF8ToString(voiceURI);

        // 設定を保存（キュー処理で使用）
        this._webSpeechSynthCurrentVoiceURI = voiceURIStr;
        this._webSpeechSynthCurrentRate = rate;
        this._webSpeechSynthCurrentPitch = pitch;

        // キューに追加
        this._webSpeechSynthQueue.push(textStr);
        console.log('[WebSpeechSynth] Enqueued: ' + textStr.substring(0, 20) + '... (Queue: ' + this._webSpeechSynthQueue.length + ')');

        // 発話中でなければ処理開始
        if (!this._webSpeechSynthIsSpeaking && this._webSpeechSynthProcessQueue) {
            this._webSpeechSynthProcessQueue();
        }
    },

    /**
     * 発話中止+キュークリア
     */
    WebSpeechSynth_Cancel: function() {
        if (!window.speechSynthesis) {
            return;
        }

        window.speechSynthesis.cancel();
        this._webSpeechSynthQueue = [];
        this._webSpeechSynthIsSpeaking = false;
        console.log('[WebSpeechSynth] Cancelled');
    },

    /**
     * 発話中かチェック
     */
    WebSpeechSynth_IsSpeaking: function() {
        return this._webSpeechSynthIsSpeaking;
    }
});
