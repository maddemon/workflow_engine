import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { TextInput, PasswordInput, Button, Paper, Text, Stack, Title, Center, Box } from '@mantine/core';
import { useAuth } from '../hooks/AuthContext.tsx';

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const result = await login({ email, password });
      if (result.success) {
        navigate('/');
      } else {
        setError(result.error ?? 'Login failed');
      }
    } catch {
      setError('An unexpected error occurred');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Center style={{ height: '100vh' }}>
      <Paper w={400} p="xl" shadow="sm" withBorder>
        <form onSubmit={handleSubmit}>
          <Stack gap="md">
            <Box>
              <Title order={3}>Sign In</Title>
              <Text size="sm" c="dimmed">Enter your credentials to continue</Text>
            </Box>
            {error && (
              <Text size="sm" c="red">{error}</Text>
            )}
            <TextInput
              label="Email"
              type="email"
              placeholder="your@email.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />
            <PasswordInput
              label="Password"
              placeholder="Your password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <Button type="submit" loading={loading} fullWidth>
              Sign In
            </Button>
            <Text size="xs" ta="center" c="dimmed">
              Default: admin@flowengine.local / admin123
            </Text>
          </Stack>
        </form>
      </Paper>
    </Center>
  );
}
