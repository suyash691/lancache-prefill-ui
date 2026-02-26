import { setToken } from '../api.js';
import { t } from '../i18n/i18n.js';

let pendingUsername = '', pendingPassword = '';

export function showLogin() {
  document.getElementById('loginOverlay').style.display = 'flex';
  showStep('stepCreds');
}

export function hideLogin() {
  document.getElementById('loginOverlay').style.display = 'none';
}

function showStep(id) {
  document.querySelectorAll('.login-step').forEach(s => s.classList.remove('active'));
  document.getElementById(id).classList.add('active');
}

export async function doLogin(onLoggedIn) {
  pendingUsername = document.getElementById('lUser').value;
  pendingPassword = document.getElementById('lPass').value;
  if (!pendingUsername || !pendingPassword) {
    document.getElementById('loginMsg').textContent = t('login.enterBoth');
    return;
  }
  showStep('stepDevice');
  try {
    const r = await (await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: pendingUsername, password: pendingPassword })
    })).json();
    if (r.success) { setToken(r.sessionToken); hideLogin(); onLoggedIn(); return; }
    if (r.next === '2fa_required' || r.next === 'email_code_required') {
      document.getElementById('codeMsg').textContent = r.next === 'email_code_required' ? t('login.codeFromEmail') : t('login.enter2fa');
      showStep('stepCode');
    } else {
      const msgs = { 'invalid_password': t('login.incorrectPassword'), 'too_many_attempts': t('login.tooMany'), 'credentials_required': t('login.enterBoth') };
      document.getElementById('loginMsg').textContent = msgs[r.next] || r.next || 'Login failed';
      showStep('stepCreds');
    }
  } catch { document.getElementById('loginMsg').textContent = t('login.connectionError'); showStep('stepCreds'); }
}

export async function submitCode(onLoggedIn) {
  const code = document.getElementById('lCode').value;
  if (!code) { document.getElementById('codeMsg').textContent = t('login.enterCode'); return; }
  try {
    const r = await (await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: pendingUsername, password: pendingPassword, twoFactorCode: code, emailCode: code })
    })).json();
    if (r.success) { setToken(r.sessionToken); hideLogin(); onLoggedIn(); }
    else document.getElementById('codeMsg').textContent = r.next === '2fa_invalid' ? t('login.invalidCode') : r.next || 'Failed';
  } catch { document.getElementById('codeMsg').textContent = t('login.connectionError'); }
}

// Export showStep for HTML onclick
export { showStep };