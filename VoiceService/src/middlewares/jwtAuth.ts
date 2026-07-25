import { Request, Response, NextFunction } from "express";
import jwt, { JwtPayload } from "jsonwebtoken";
import config from "../config";

type TokenClaims = JwtPayload & {
  sub?: string;
  unique_name?: string;
  name?: string;
};

export function jwtAuth(req: Request, res: Response, next: NextFunction): void {
  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith("Bearer ")) {
    res.status(401).json({ error: "Missing or invalid Authorization header" });
    return;
  }

  const token = authHeader.slice("Bearer ".length).trim();

  try {
    const decoded = jwt.verify(token, config.jwt.key, {
      issuer: config.jwt.issuer,
      audience: config.jwt.audience,
    }) as TokenClaims;

    const userId = decoded.sub;
    const userName = decoded.unique_name ?? decoded.name;

    if (!userId || !userName) {
      res.status(401).json({ error: "Token does not include required claims" });
      return;
    }

    res.locals.user = { userId, userName };
    res.locals.rawToken = token;
    next();
  } catch {
    res.status(401).json({ error: "Invalid token" });
  }
}
