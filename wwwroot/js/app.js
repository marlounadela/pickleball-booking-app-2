window.pbChart = {
    charts: {},
    create: function (id, config) {
        var el = document.getElementById(id);
        if (!el) return;
        if (this.charts[id]) { this.charts[id].destroy(); delete this.charts[id]; }
        this.charts[id] = new Chart(el, config);
    },
    destroy: function (id) {
        if (this.charts[id]) { this.charts[id].destroy(); delete this.charts[id]; }
    }
};

window.pbDownload = {
    csv: function (filename, content) {
        var blob = new Blob(["\uFEFF" + content], { type: 'text/csv;charset=utf-8;' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },
    text: function (filename, content) {
        var blob = new Blob([content], { type: 'text/plain;charset=utf-8;' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
};

window.pbPrint = function () { window.print(); };

window.pbCopy = function (text) {
    navigator.clipboard.writeText(text).catch(function () { });
};