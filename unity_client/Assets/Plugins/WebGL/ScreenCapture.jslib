// ScreenCapture.jslib
// ブラウザのScreen Capture API（getDisplayMedia）を使用して
// デスクトップ/ウィンドウの映像をキャプチャし、Unityテクスチャとして提供する

mergeInto(LibraryManager.library, {

    // 内部状態（グローバル）
    $ScreenCaptureState: {
        stream: null,
        video: null,
        canvas: null,
        ctx: null,
        animFrameId: 0,
        bufferPtr: 0,
        bufferSize: 0,
        width: 0,
        height: 0,
        isCapturing: false,
        frameReady: false
    },

    // $ScreenCaptureState を他の関数から参照可能にする
    ScreenCapture_Start__deps: ['$ScreenCaptureState'],
    ScreenCapture_Stop__deps: ['$ScreenCaptureState'],
    ScreenCapture_IsCapturing__deps: ['$ScreenCaptureState'],
    ScreenCapture_IsFrameReady__deps: ['$ScreenCaptureState'],
    ScreenCapture_GetWidth__deps: ['$ScreenCaptureState'],
    ScreenCapture_GetHeight__deps: ['$ScreenCaptureState'],
    ScreenCapture_UpdateBuffer__deps: ['$ScreenCaptureState'],

    /**
     * 画面キャプチャを開始
     * ブラウザの画面共有ダイアログを表示し、ユーザーが選択した画面/ウィンドウを取得
     * @param {number} maxWidth - キャプチャ解像度の最大幅
     * @param {number} maxHeight - キャプチャ解像度の最大高さ
     * @param {string} callbackObjectName - 結果コールバック先のGameObject名
     * @param {string} callbackMethodName - コールバックメソッド名（string引数: "ok" or エラーメッセージ）
     */
    ScreenCapture_Start: function(maxWidth, maxHeight, callbackObjectName, callbackMethodName) {
        var objName = UTF8ToString(callbackObjectName);
        var methodName = UTF8ToString(callbackMethodName);
        var state = ScreenCaptureState;

        if (state.isCapturing) {
            console.log('[ScreenCapture] Already capturing');
            SendMessage(objName, methodName, 'ok');
            return;
        }

        // getDisplayMedia がサポートされているか確認
        if (!navigator.mediaDevices || !navigator.mediaDevices.getDisplayMedia) {
            console.error('[ScreenCapture] getDisplayMedia not supported');
            SendMessage(objName, methodName, 'getDisplayMedia not supported in this browser');
            return;
        }

        navigator.mediaDevices.getDisplayMedia({
            video: {
                width: { ideal: maxWidth },
                height: { ideal: maxHeight },
                frameRate: { ideal: 15, max: 30 }
            },
            audio: false
        }).then(function(stream) {
            state.stream = stream;

            // 非表示のvideo要素を作成してストリームを接続
            var video = document.createElement('video');
            video.srcObject = stream;
            video.autoplay = true;
            video.muted = true;
            video.playsInline = true;
            video.style.display = 'none';
            document.body.appendChild(video);
            state.video = video;

            // キャプチャ用canvasを作成
            var canvas = document.createElement('canvas');
            canvas.style.display = 'none';
            document.body.appendChild(canvas);
            state.canvas = canvas;
            state.ctx = canvas.getContext('2d', { willReadFrequently: true });

            // ストリーム終了検知（ユーザーが共有を停止した場合）
            stream.getVideoTracks()[0].addEventListener('ended', function() {
                console.log('[ScreenCapture] Stream ended by user');
                // Unity側に通知
                SendMessage(objName, 'OnScreenCaptureStopped', '');
            });

            // videoのメタデータが読み込まれたらキャプチャループ開始
            video.addEventListener('loadedmetadata', function() {
                // 実際の解像度を maxWidth/maxHeight に収める
                var srcW = video.videoWidth;
                var srcH = video.videoHeight;
                var scale = Math.min(maxWidth / srcW, maxHeight / srcH, 1.0);
                state.width = Math.floor(srcW * scale);
                state.height = Math.floor(srcH * scale);

                canvas.width = state.width;
                canvas.height = state.height;

                // ピクセルバッファを確保（RGBA）
                state.bufferSize = state.width * state.height * 4;
                state.bufferPtr = _malloc(state.bufferSize);

                state.isCapturing = true;
                console.log('[ScreenCapture] Started: ' + state.width + 'x' + state.height +
                            ' (source: ' + srcW + 'x' + srcH + ')');

                // フレーム取得ループ開始
                function captureFrame() {
                    if (!state.isCapturing) return;

                    if (video.readyState >= video.HAVE_CURRENT_DATA) {
                        state.ctx.drawImage(video, 0, 0, state.width, state.height);
                        state.frameReady = true;
                    }

                    state.animFrameId = requestAnimationFrame(captureFrame);
                }
                captureFrame();

                SendMessage(objName, methodName, 'ok');
            });

            video.play();

        }).catch(function(err) {
            console.error('[ScreenCapture] Failed: ' + err.message);
            SendMessage(objName, methodName, err.message);
        });
    },

    /**
     * 画面キャプチャを停止
     */
    ScreenCapture_Stop: function() {
        var state = ScreenCaptureState;

        state.isCapturing = false;
        state.frameReady = false;

        if (state.animFrameId) {
            cancelAnimationFrame(state.animFrameId);
            state.animFrameId = 0;
        }

        if (state.stream) {
            state.stream.getTracks().forEach(function(track) { track.stop(); });
            state.stream = null;
        }

        if (state.video) {
            state.video.srcObject = null;
            if (state.video.parentNode) state.video.parentNode.removeChild(state.video);
            state.video = null;
        }

        if (state.canvas) {
            if (state.canvas.parentNode) state.canvas.parentNode.removeChild(state.canvas);
            state.canvas = null;
            state.ctx = null;
        }

        if (state.bufferPtr) {
            _free(state.bufferPtr);
            state.bufferPtr = 0;
            state.bufferSize = 0;
        }

        state.width = 0;
        state.height = 0;

        console.log('[ScreenCapture] Stopped');
    },

    /**
     * キャプチャ中かどうか
     * @returns {number} 1=キャプチャ中, 0=停止中
     */
    ScreenCapture_IsCapturing: function() {
        return ScreenCaptureState.isCapturing ? 1 : 0;
    },

    /**
     * 新しいフレームが利用可能かどうか
     * @returns {number} 1=利用可能, 0=なし
     */
    ScreenCapture_IsFrameReady: function() {
        return ScreenCaptureState.frameReady ? 1 : 0;
    },

    /**
     * キャプチャ解像度（幅）
     */
    ScreenCapture_GetWidth: function() {
        return ScreenCaptureState.width;
    },

    /**
     * キャプチャ解像度（高さ）
     */
    ScreenCapture_GetHeight: function() {
        return ScreenCaptureState.height;
    },

    /**
     * 現在のフレームのピクセルデータをjslib内部バッファに書き込む
     * Unity側からはGetBufferPtr()でバッファポインタを取得してLoadRawTextureDataに渡す
     * @returns {number} 1=成功（新フレーム書き込み済み）, 0=失敗/フレームなし
     */
    ScreenCapture_UpdateBuffer: function() {
        var state = ScreenCaptureState;

        if (!state.isCapturing || !state.frameReady || !state.ctx) return 0;
        if (!state.bufferPtr || state.bufferSize <= 0) return 0;

        var imageData = state.ctx.getImageData(0, 0, state.width, state.height);
        HEAPU8.set(imageData.data, state.bufferPtr);

        state.frameReady = false;
        return 1;
    },

    ScreenCapture_GetBufferPtr__deps: ['$ScreenCaptureState'],

    /**
     * 内部バッファのポインタを取得（Unity側でLoadRawTextureData(IntPtr)に使用）
     * @returns {number} バッファポインタ（0=未確保）
     */
    ScreenCapture_GetBufferPtr: function() {
        return ScreenCaptureState.bufferPtr;
    },

    ScreenCapture_GetBufferSize__deps: ['$ScreenCaptureState'],

    /**
     * 内部バッファのサイズを取得
     * @returns {number} バッファサイズ（バイト）
     */
    ScreenCapture_GetBufferSize: function() {
        return ScreenCaptureState.bufferSize;
    }
});
