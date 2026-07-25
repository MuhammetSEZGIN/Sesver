import { Request, Response, NextFunction } from "express";
import { ILiveKitService } from "../services/LiveKitService";
import { IDmAuthorizationService, isDmRoomId, extractConversationId } from "../services/DmAuthorizationService";

export class VoiceController {
  private readonly liveKitService: ILiveKitService;
  private readonly dmAuthorizationService: IDmAuthorizationService;

  constructor(liveKitService: ILiveKitService, dmAuthorizationService: IDmAuthorizationService) {
    this.liveKitService = liveKitService;
    this.dmAuthorizationService = dmAuthorizationService;
  }

  /**
   * GET /api/voice/join-room/:roomId
   *
   * Dönüş: { token: string }
   */
  joinRoom = async (
    req: Request,
    res: Response,
    next: NextFunction
  ): Promise<void> => {
    try {
      const roomId = req.params.roomId as string;
      const user = res.locals.user as { userId: string; userName: string } | undefined;
      const rawToken = res.locals.rawToken as string | undefined;

      if (!roomId) {
        res.status(400).json({ error: "roomId is required (route param)" });
        return;
      }

      if (!user || !rawToken) {
        res.status(401).json({ error: "Unauthorized user context" });
        return;
      }

      if (isDmRoomId(roomId)) {
        const conversationId = extractConversationId(roomId);
        const authorized = await this.dmAuthorizationService.isParticipant(conversationId, rawToken);
        if (!authorized) {
          res.status(403).json({ error: "Not a participant of this DM conversation" });
          return;
        }
      }

      const token = await this.liveKitService.generateRoomToken(
        roomId,
        user.userId,
        user.userName
      );

      res.json({ token });
    } catch (err) {
      next(err);
    }
  };
}
