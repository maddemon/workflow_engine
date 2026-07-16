export interface DogfoodingConfig {
  flowEngine: {
    url: string;
    apiKey: string;
  };
  scenariosPerRound: number;
  maxBuildRetries: number;
  maxExecRetries: number;
  knowledgeBaseDir: string;
}

export const defaultConfig: DogfoodingConfig = {
  flowEngine: {
    url: process.env.FLOWENGINE_URL || 'http://localhost:8001',
    apiKey: process.env.FLOWENGINE_API_KEY || '',
  },
  scenariosPerRound: parseInt(process.env.DOGFOODING_SCENARIOS_PER_ROUND || '3'),
  maxBuildRetries: 3,
  maxExecRetries: 2,
  knowledgeBaseDir: process.env.DOGFOODING_KB_DIR || 'docs/superpowers/dogfooding',
};
