mergeInto(LibraryManager.library, {
    GetJSHeapUsedMB: function () {
        if (performance && performance.memory) {
            return performance.memory.usedJSHeapSize / (1024 * 1024);
        }
        return 0.0;
    },

    GetJSHeapTotalMB: function () {
        if (performance && performance.memory) {
            return performance.memory.totalJSHeapSize / (1024 * 1024);
        }
        return 0.0;
    }
});
