import { defaultConfig, type DogfoodingConfig } from '../config.default.js';

export class LlmClient {
  private config: DogfoodingConfig['llm'];

  constructor(config?: DogfoodingConfig['llm']) {
    this.config = config ?? defaultConfig.llm;
  }

  async generate(prompt: string, options?: { system?: string; temperature?: number }): Promise<string> {
    const messages: Array<{ role: string; content: string }> = [];
    if (options?.system) {
      messages.push({ role: 'system', content: options.system });
    }
    messages.push({ role: 'user', content: prompt });

    const response = await fetch(`${this.config.baseUrl}/chat/completions`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${this.config.apiKey}`,
      },
      body: JSON.stringify({
        model: this.config.model,
        messages,
        temperature: options?.temperature ?? 0.7,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(`LLM API ${response.status}: ${(err as { error?: { message?: string } })?.error?.message ?? response.statusText}`);
    }

    const data = await response.json() as { choices: Array<{ message: { content: string } }> };
    return data.choices[0].message.content;
  }

  async generateJson<T>(prompt: string, options?: { system?: string; temperature?: number }): Promise<T> {
    const systemMsg = (options?.system ?? '') +
      '\n\n你必须只输出 JSON，不要包含 markdown 代码块标记、不要有额外文字。';
    const text = await this.generate(prompt, { ...options, system: systemMsg, temperature: options?.temperature ?? 0.3 });
    return JSON.parse(text) as T;
  }
}
