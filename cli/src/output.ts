export interface OutputOptions {
  json: boolean;
  verbose: boolean;
}

const defaultOptions: OutputOptions = {
  json: false,
  verbose: false,
};

let currentOptions: OutputOptions = { ...defaultOptions };

export function setOutputOptions(options: Partial<OutputOptions>): void {
  currentOptions = { ...currentOptions, ...options };
}

export function getOutputOptions(): Readonly<OutputOptions> {
  return { ...currentOptions };
}

export function isJsonMode(): boolean {
  return currentOptions.json;
}

export function isVerbose(): boolean {
  return currentOptions.verbose;
}

export function writeJson(data: unknown): void {
  console.log(JSON.stringify(data, undefined, 2));
}

export function log(message: string): void {
  if (currentOptions.json) {
    console.error(message);
    return;
  }
  console.log(message);
}

export function error(message: string): void {
  console.error(message);
}

export function verbose(message: string): void {
  if (!currentOptions.verbose) {
    return;
  }
  console.error(`[verbose] ${message}`);
}
