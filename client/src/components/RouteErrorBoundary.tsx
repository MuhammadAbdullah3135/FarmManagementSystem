import React from 'react';
import { Button, Result, Typography } from 'antd';
import { Link } from 'react-router-dom';

const { Paragraph, Text } = Typography;

interface RouteErrorBoundaryProps {
  /** The path this boundary guards, reported with the error so a screenshot identifies the screen. */
  routePath: string;
  children: React.ReactNode;
}

interface RouteErrorBoundaryState {
  error: Error | null;
}

/**
 * Keeps one screen's render error inside that screen.
 *
 * The root ErrorBoundary is still the last resort, but it replaces the entire app — which is
 * how a single bad field on one report (a null amount formatted as money) turned into a dead
 * end with no header, no navigation and nothing to do but reload. This one sits inside the
 * layout, so the shell survives, the user can move to another screen, and the route is named
 * in the message instead of left to guesswork.
 */
class RouteErrorBoundary extends React.Component<RouteErrorBoundaryProps, RouteErrorBoundaryState> {
  state: RouteErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): RouteErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error(
      `[RouteErrorBoundary] Render error on ${this.props.routePath}:`,
      error,
      info.componentStack,
    );
  }

  handleTryAgain = () => {
    // Re-rendering is often enough: the failure may have been one bad payload, and a reload
    // would throw away the shell for a screen that can recover on its own.
    this.setState({ error: null });
  };

  render() {
    const { error } = this.state;
    if (!error) {
      return this.props.children;
    }

    return (
      <Result
        status="error"
        title="This screen hit an error"
        subTitle={
          <>
            <Paragraph style={{ marginBottom: 8 }}>
              The rest of the app is still working — pick another screen from the menu, or try
              this one again.
            </Paragraph>
            <Paragraph style={{ marginBottom: 0 }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                {this.props.routePath} · {error.message || String(error)}
              </Text>
            </Paragraph>
          </>
        }
        extra={[
          <Button key="retry" type="primary" onClick={this.handleTryAgain}>
            Try again
          </Button>,
          <Link key="dashboard" to="/dashboard">
            <Button>Back to dashboard</Button>
          </Link>,
        ]}
      />
    );
  }
}

export default RouteErrorBoundary;
