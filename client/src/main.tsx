import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { initOfflineMonitoring } from './offline/connectivity'
import { initSyncEngine } from './offline/syncEngine'
import { registerOfflineShell } from './offline/registerServiceWorker'

// Connectivity listeners and the store's current size. Started before the first render
// so the offline banner is correct on the very first paint after an offline launch,
// rather than appearing a frame later.
initOfflineMonitoring()

// The write queue's triggers (connectivity, focus, a successful request, a token refresh)
// and one flush attempt at boot — a device that has been offline for a week sends its work
// without the user having to find a button. Deliberately not awaited: boot never blocks on
// the network.
initSyncEngine()

// Registers the precaching worker in production builds only. Deliberately not awaited:
// boot must not wait on the network, and a worker that fails to register leaves an app
// that works exactly as it did before offline support existed.
void registerOfflineShell()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)
