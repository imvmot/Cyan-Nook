// FileIO.jslib
// ブラウザ上でのファイルダウンロード・アップロード処理

mergeInto(LibraryManager.library, {

    /**
     * JSONテキストをファイルとしてダウンロード
     * @param {string} filename - ダウンロードファイル名
     * @param {string} content - ファイル内容（JSON文字列）
     */
    FileIO_Download: function(filename, content) {
        var filenameStr = UTF8ToString(filename);
        var contentStr = UTF8ToString(content);

        var blob = new Blob([contentStr], { type: 'application/json' });
        var url = URL.createObjectURL(blob);

        var a = document.createElement('a');
        a.href = url;
        a.download = filenameStr;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();

        // クリーンアップ
        setTimeout(function() {
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }, 100);

        console.log('[FileIO] Download triggered: ' + filenameStr);
    },

    /**
     * ファイル選択ダイアログを開いてテキストファイルを読み込む
     * 読み込み完了後、指定GameObjectのメソッドにコールバック
     * @param {string} callbackObjectName - コールバック先のGameObject名
     * @param {string} callbackMethodName - コールバックメソッド名（string引数）
     * @param {string} accept - 受け入れるファイルタイプ（".json"等）
     */
    FileIO_OpenFileDialog: function(callbackObjectName, callbackMethodName, accept) {
        var objName = UTF8ToString(callbackObjectName);
        var methodName = UTF8ToString(callbackMethodName);
        var acceptStr = UTF8ToString(accept);

        var input = document.createElement('input');
        input.type = 'file';
        input.accept = acceptStr;
        input.style.display = 'none';
        document.body.appendChild(input);

        input.addEventListener('change', function(event) {
            var file = event.target.files[0];
            if (!file) {
                document.body.removeChild(input);
                return;
            }

            var reader = new FileReader();
            reader.onload = function(e) {
                var content = e.target.result;
                console.log('[FileIO] File loaded: ' + file.name + ' (' + content.length + ' chars)');
                SendMessage(objName, methodName, content);
                document.body.removeChild(input);
            };
            reader.onerror = function() {
                console.error('[FileIO] File read error');
                SendMessage(objName, methodName, '');
                document.body.removeChild(input);
            };
            reader.readAsText(file);
        });

        input.click();
    }
});
