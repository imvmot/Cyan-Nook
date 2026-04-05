// MobileKeyboard.jslib
// iOS Safariのソフトウェアキーボード表示/非表示を検出し、
// visualViewport APIでキーボード高さをUnityに通知する

mergeInto(LibraryManager.library, {

    /**
     * visualViewportのリサイズイベントを監視開始
     * キーボード表示/非表示時にC#側にキーボード高さを通知
     * @param {string} callbackObjectName - コールバック先のGameObject名
     */
    MobileKeyboard_Register: function(callbackObjectName) {
        var objName = UTF8ToString(callbackObjectName);

        // 既存のリスナーがあれば解除
        if (window._mobileKeyboardListener) {
            window.visualViewport.removeEventListener('resize', window._mobileKeyboardListener);
        }

        if (!window.visualViewport) {
            console.warn('[MobileKeyboard] visualViewport API not available');
            return;
        }

        window._mobileKeyboardObjName = objName;

        window._mobileKeyboardListener = function() {
            var keyboardHeight = window.innerHeight - window.visualViewport.height;
            // 小さな差異（数px以下）は無視（スクロールバー等のノイズ）
            if (keyboardHeight < 10) keyboardHeight = 0;
            SendMessage(window._mobileKeyboardObjName, 'OnKeyboardHeightChanged', keyboardHeight.toString());
        };

        window.visualViewport.addEventListener('resize', window._mobileKeyboardListener);
        console.log('[MobileKeyboard] Registered visualViewport listener');
    },

    /**
     * visualViewportのリサイズイベント監視を解除
     */
    MobileKeyboard_Unregister: function() {
        if (window._mobileKeyboardListener && window.visualViewport) {
            window.visualViewport.removeEventListener('resize', window._mobileKeyboardListener);
            window._mobileKeyboardListener = null;
            console.log('[MobileKeyboard] Unregistered visualViewport listener');
        }
    }
});
