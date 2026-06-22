import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { TextInput, PasswordInput, Button, Paper, Text, Stack, Title, Anchor, Center, Box } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useAuth } from '../hooks/AuthContext.tsx';

export function RegisterPage() {
  const [email, setEmail] = useState('');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { register } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (password !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    if (password.length < 8) {
      setError('Password must be at least 8 characters');
      return;
    }

    setLoading(true);
    try {
      const result = await register({ email, password, userName });
      if (result.success) {
        notifications.show({
          title: 'Registered',
          message: 'Account created successfully. You can now sign in.',
          color: 'green',
        });
        navigate('/login');
      } else {
        setError(result.errorMessage ?? 'Registration failed');
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
              <Title order={3}>Create Account</Title>
              <Text size="sm" c="dimmed">Register to get started</Text>
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
            />
            <TextInput
              label="Username"
              placeholder="Your username"
              value={userName}
              onChange={(e) => setUserName(e.target.value)}
              required
            />
            <PasswordInput
              label="Password"
              placeholder="Min 8 characters"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <PasswordInput
              label="Confirm Password"
              placeholder="Repeat password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              required
            />
            <Button type="submit" loading={loading} fullWidth>
              Register
            </Button>
            <Text size="xs" ta="center">
              Already have an account?{' '}
              <Anchor component={Link} to="/login">Sign in</Anchor>
            </Text>
          </Stack>
        </form>
      </Paper>
    </Center>
  );
}
