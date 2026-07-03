import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { TextInput, PasswordInput, Button, Paper, Text, Stack, Title, Anchor, Center, Box, List, ThemeIcon } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { Check, X } from 'lucide-react';
import { useAuth } from '../hooks/AuthContext.tsx';

interface PasswordRequirement {
  label: string;
  met: boolean;
}

export function RegisterPage() {
  const [email, setEmail] = useState('');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { register } = useAuth();
  const navigate = useNavigate();

  const requirements: PasswordRequirement[] = [
    { label: 'At least 8 characters', met: password.length >= 8 },
    { label: 'At least one uppercase letter', met: /[A-Z]/.test(password) },
    { label: 'At least one lowercase letter', met: /[a-z]/.test(password) },
    { label: 'At least one digit', met: /[0-9]/.test(password) },
    { label: 'At least one special character', met: /[^a-zA-Z0-9]/.test(password) },
  ];

  const allRequirementsMet = requirements.every((r) => r.met);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (password !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    if (!allRequirementsMet) {
      setError('Password does not meet all strength requirements');
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
            <Box>
              <Text size="xs" fw={500} mb="xs">Password requirements</Text>
              <List spacing="xs" size="xs" center={false}>
                {requirements.map((req) => (
                  <List.Item
                    key={req.label}
                    icon={
                      <ThemeIcon color={req.met ? 'green' : 'red'} size={16} radius="xl">
                        {req.met ? <Check size={12} /> : <X size={12} />}
                      </ThemeIcon>
                    }
                  >
                    <Text c={req.met ? 'green' : 'red'}>{req.label}</Text>
                  </List.Item>
                ))}
              </List>
            </Box>
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
