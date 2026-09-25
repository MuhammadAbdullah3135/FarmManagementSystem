import React from 'react';
import i18n from '../i18n';

interface ErrorBoundaryProps {
  children: React.ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Top-level crash guard. Without this, any render error unmounts the whole
 * React tree and the user sees a blank white page (observed on older Android
 * WebViews after login). Instead, show the actual error on-screen with a
 * Reload action so the failure is diagnosable in the field.
 */
class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    // Surface in the console; the APK's Crashlytics hook also captures JS crashes.
    console.error('[ErrorBoundary] Uncaught render error:', error, info.componentStack);
  }

  handleReload = () => {
    window.location.reload();
  };

  render() {
    const { error } = this.state;
    // A class component cannot use the `useTranslation` hook, and this screen exists
    // precisely when the tree below it has crashed — so it reads the live language straight
    // from the i18n instance rather than subscribing to anything.
    const t = i18n.getFixedT(null, 'errors');
    if (error) {
      return (
        <div
          role="alert"
          style={{
            minHeight: '100vh',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: '#f0f2f5',
            padding: 24,
            fontFamily: 'system-ui, sans-serif',
          }}
        >
          <div
            style={{
              maxWidth: 520,
              width: '100%',
              background: '#fff',
              borderRadius: 8,
              boxShadow: '0 1px 4px rgba(0,0,0,0.12)',
              padding: 24,
            }}
          >
            <h2 style={{ margin: '0 0 8px', fontSize: 20 }}>{t('somethingWentWrong')}</h2>
            <p style={{ margin: '0 0 12px', color: '#555' }}>{t('unexpectedRenderError')}</p>
            <pre
              style={{
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
                background: '#f6f6f6',
                border: '1px solid #eee',
                borderRadius: 6,
                padding: 12,
                fontSize: 12,
                maxHeight: 180,
                overflow: 'auto',
                margin: '0 0 16px',
              }}
            >
              {error.message || String(error)}
            </pre>
            <button
              type="button"
              onClick={this.handleReload}
              style={{
                background: '#1677ff',
                color: '#fff',
                border: 'none',
                borderRadius: 6,
                padding: '10px 20px',
                fontSize: 15,
                cursor: 'pointer',
              }}
            >
              {t('reloadApp')}
            </button>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}

export default ErrorBoundary;
