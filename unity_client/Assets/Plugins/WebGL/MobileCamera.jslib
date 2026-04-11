var MobileCameraPlugin = {

    $MobileCameraState: {
        ready: 0,      // 0=pending, 1=ready
        label: ""      // 結果のデバイスラベル（空文字=見つからず）
    },

    /**
     * 背面カメラの検索を開始する（非同期）。
     * enumerateDevices()でカメラ一覧を取得し、ラベルから背面カメラを特定する。
     * getUserMedia()と異なりストリームを開かないため、
     * iOS Safari で WebCamTexture が既にカメラを使用中でもハングしない。
     * 結果はMobileCamera_IsReady/GetLabelでポーリング取得する。
     */
    MobileCamera_FindRearCamera: function() {
        MobileCameraState.ready = 0;
        MobileCameraState.label = "";

        console.log("[MobileCamera] Enumerating devices...");

        function findRearInList(videoDevices) {
            // 背面カメラをラベルから検索（正のマッチ）
            for (var i = 0; i < videoDevices.length; i++) {
                var label = videoDevices[i].label.toLowerCase();
                if (label.indexOf("back") >= 0 || label.indexOf("rear") >= 0 ||
                    label.indexOf("\u80CC\u9762") >= 0 || label.indexOf("environment") >= 0) {
                    console.log("[MobileCamera] Rear camera found by label: " + videoDevices[i].label);
                    return videoDevices[i].label;
                }
            }
            // 正のマッチなし → 前面でないデバイスを検索
            for (var i = 0; i < videoDevices.length; i++) {
                var label = videoDevices[i].label.toLowerCase();
                if (label.indexOf("front") < 0 && label.indexOf("\u524D\u9762") < 0 &&
                    label.indexOf("user") < 0 && label.indexOf("selfie") < 0) {
                    console.log("[MobileCamera] Non-front camera found: " + videoDevices[i].label);
                    return videoDevices[i].label;
                }
            }
            return null;
        }

        navigator.mediaDevices.enumerateDevices()
        .then(function(devices) {
            var videoDevices = devices.filter(function(d) { return d.kind === "videoinput"; });
            console.log("[MobileCamera] Found " + videoDevices.length + " video devices");

            for (var i = 0; i < videoDevices.length; i++) {
                console.log("[MobileCamera]   [" + i + "] label=\"" + videoDevices[i].label + "\"");
            }

            // ラベルが空 = カメラ権限未付与。先にgetUserMediaで権限取得してから再列挙
            var hasLabels = videoDevices.some(function(d) { return d.label && d.label.length > 0; });
            if (!hasLabels && videoDevices.length > 0) {
                console.log("[MobileCamera] No labels (no permission yet), requesting camera access...");
                return navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: "environment" } }
                })
                .then(function(stream) {
                    stream.getTracks().forEach(function(t) { t.stop(); });
                    console.log("[MobileCamera] Permission granted, re-enumerating...");
                    return navigator.mediaDevices.enumerateDevices();
                })
                .then(function(devices2) {
                    var videoDevices2 = devices2.filter(function(d) { return d.kind === "videoinput"; });
                    for (var i = 0; i < videoDevices2.length; i++) {
                        console.log("[MobileCamera]   [" + i + "] label=\"" + videoDevices2[i].label + "\"");
                    }
                    var found = findRearInList(videoDevices2);
                    MobileCameraState.label = found || "";
                    MobileCameraState.ready = 1;
                });
            }

            var found = findRearInList(videoDevices);
            if (found !== null) {
                MobileCameraState.label = found;
                MobileCameraState.ready = 1;
            } else {
                console.warn("[MobileCamera] No rear camera found in device list");
                MobileCameraState.label = "";
                MobileCameraState.ready = 1;
            }
        })
        .catch(function(err) {
            console.warn("[MobileCamera] Failed: " + err.name + ": " + err.message);
            MobileCameraState.label = "";
            MobileCameraState.ready = 1;
        });
    },

    /**
     * 検索結果が準備できたか（0=pending, 1=ready）
     */
    MobileCamera_IsReady: function() {
        return MobileCameraState.ready;
    },

    /**
     * 検索結果のデバイスラベルを返す（UTF8文字列、_malloc確保）
     */
    MobileCamera_GetLabel: function() {
        var str = MobileCameraState.label || "";
        var size = lengthBytesUTF8(str) + 1;
        var buf = _malloc(size);
        stringToUTF8(str, buf, size);
        return buf;
    }
};

autoAddDeps(MobileCameraPlugin, '$MobileCameraState');
mergeInto(LibraryManager.library, MobileCameraPlugin);
