import dotenv from "dotenv";
dotenv.config();

interface Config {
  port: number;
  jwt: {
    key: string;
    issuer: string;
    audience: string;
  };
  livekit: {
    apiKey: string;
    apiSecret: string;
    url: string;
  };
  messageServiceUrl: string;
}

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

const config: Config = {
  port: parseInt(process.env.PORT ?? "4000", 10),
  jwt: {
    key: requireEnv("JWT_KEY"),
    issuer: requireEnv("JWT_ISSUER"),
    audience: requireEnv("JWT_AUDIENCE"),
  },
  livekit: {
    apiKey: requireEnv("LIVEKIT_API_KEY"),
    apiSecret: requireEnv("LIVEKIT_API_SECRET"),
    url: requireEnv("LIVEKIT_URL"),
  },
  messageServiceUrl: process.env.MESSAGE_SERVICE_URL ?? "http://localhost:5107",
};

export default config;
