// Clipboard.jslib
// ブラウザのClipboard APIを使用したコピー・ペースト処理
// iOS SafariではユーザージェスチャーからのAPI呼び出しが必要

mergeInto(LibraryManager.library, {

    /**
     * テキストをクリップボードにコピー
     * @param {string} text - コピーするテキスト
     */
    Clipboard_CopyText: function(text) {
        var textStr = UTF8ToString(text);

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(textStr).then(function() {
                console.log('[Clipboard] Copy success');
            }).catch(function(err) {
                console.warn('[Clipboard] Copy failed: ' + err);
            });
        } else {
            // フォールバック: 非対���ブラウザ
            console.warn('[Clipboard] Clipboard API not available');
        }
    },

    /**
     * クリップボードからテキストを読���取り、SendMessageでC#に返す
     * @param {string} callbackObjectName - コールバック先のGameObject名
     * @param {string} callbackMethodName - コール��ックメソッド名（string引数）
     */
    Clipboard_RequestPaste: function(callbackObjectName, callbackMethodName) {
        var objName = UTF8ToString(callbackObjectName);
        var methodName = UTF8ToString(callbackMethodName);

        if (navigator.clipboard && navigator.clipboard.readText) {
            navigator.clipboard.readText().then(function(text) {
                console.log('[Clipboard] Paste success (' + text.length + ' chars)');
                SendMessage(objName, methodName, text);
            }).catch(function(err) {
                console.warn('[Clipboard] Paste failed: ' + err);
                SendMessage(objName, methodName, '');
            });
        } else {
            console.warn('[Clipboard] Clipboard API not available');
            SendMessage(objName, methodName, '');
        }
    }
});
