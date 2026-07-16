import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { TextInput, PasswordInput, Button, Paper, Text, Stack, Title, Center, Box } from '@mantine/core';
import { useRequest } from 'ahooks';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../hooks/AuthContext.tsx';

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const { login } = useAuth();
  const navigate = useNavigate();
  const { t } = useTranslation('login');

  const { loading, run: handleSubmit } = useRequest(
    async (e: React.FormEvent) => {
      e.preventDefault();
      setError('');
      const result = await login({ email, password });
      if (result.success) {
        navigate('/');
      } else {
        setError(result.error ?? t('failed'));
      }
    },
    {
      manual: true,
      onError: () => setError(t('unexpectedError')),
    },
  );

  return (
    <Center style={{ height: '100vh' }}>
      <Paper w={400} p="xl" shadow="sm" withBorder>
        <form onSubmit={handleSubmit}>
          <Stack gap="md">
            <Box>
              <Title order={3}>{t('title')}</Title>
              <Text size="sm" c="dimmed">{t('subtitle')}</Text>
            </Box>
            {error && (
              <Text size="sm" c="red">{error}</Text>
            )}
            <TextInput
              label={t('email')}
              type="email"
              placeholder={t('email')}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />
            <PasswordInput
              label={t('password')}
              placeholder={t('password')}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <Button type="submit" loading={loading} fullWidth>
              {t('signIn')}
            </Button>
          </Stack>
        </form>
      </Paper>
    </Center>
  );
}
