let _resolve = null;

export function showConfirm(title, msg) {
  return new Promise(resolve => {
    _resolve = resolve;
    document.getElementById('confirmTitle').textContent = title;
    document.getElementById('confirmMsg').textContent = msg;
    document.getElementById('confirmDialog').style.display = 'flex';
    document.getElementById('confirmYes').onclick = () => {
      _resolve = null; // Prevent closeConfirm from also resolving
      document.getElementById('confirmDialog').style.display = 'none';
      resolve(true);
    };
  });
}

export function closeConfirm() {
  document.getElementById('confirmDialog').style.display = 'none';
  if (_resolve) { _resolve(false); _resolve = null; }
}