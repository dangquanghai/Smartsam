(function () {
    function selectedId() {
        var selected = document.querySelector('#itemCheckingTable input[name="selectedChecking"]:checked');
        return selected ? selected.closest('tr').getAttribute('data-id') : '';
    }
    function wire(id, mode) {
        var btn = document.getElementById(id);
        if (!btn) return;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            var checkingId = selectedId();
            if (!checkingId) { alert('Please select a voucher.'); return; }
            window.location.href = 'ItemCheckingDetail?id=' + encodeURIComponent(checkingId) + '&mode=' + mode;
        });
    }
    wire('btnEdit', 'edit');
    wire('btnView', 'view');
})();
