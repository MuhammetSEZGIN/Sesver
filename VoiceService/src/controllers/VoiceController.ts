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
      const clanId = req.params.clanId as string | undefined;
      const user = res.locals.user as { userId: string; userName: string } | undefined;
      const clanRole = res.locals.clanRole as string | undefined;

      if (!roomId) {
        res.status(400).json({ error: "roomId is required (route param)" });
        return;
      }

      if (!user) {
        res.status(401).json({ error: "Unauthorized user context" });
        return;
      }

      if (isDmRoomId(roomId)) {
        const conversationId = extractConversationId(roomId);
        const authorized = await this.dmAuthorizationService.isParticipant(
          conversationId,
          user.userId
        );
        if (!authorized) {
          res.status(403).json({ error: "Not a participant of this DM conversation" });
          return;
        }
      } else {
        if (!clanId) {
          res.status(400).json({
            error: "Clan voice rooms require /join-room/{roomId}/clanId/{clanId}",
          });
          return;
        }

        if (!clanRole || !["OWNER", "ADMIN", "MEMBER"].includes(clanRole)) {
          res.status(403).json({ error: "You are not a member of this clan" });
          return;
        }

      }

      const liveKitRoomId = clanId && !isDmRoomId(roomId)
        ? this.getClanRoomId(clanId, roomId)
        : roomId;
      const token = await this.liveKitService.generateRoomToken(
        liveKitRoomId,
        user.userId,
        user.userName
      );

      res.json({ token });
    } catch (err) {
      next(err);
    }
  };

  kickParticipant = async (
    req: Request,
    res: Response,
    next: NextFunction
  ): Promise<void> => {
    try {
      const roomId = req.params.roomId as string;
      const targetUserId = req.params.userId as string;
      const clanId = req.params.clanId as string;
      const user = res.locals.user as { userId: string; userName: string } | undefined;
      const clanRole = res.locals.clanRole as string | undefined;

      if (!user) {
        res.status(401).json({ error: "Unauthorized user context" });
        return;
      }

      if (!roomId || !targetUserId || !clanId) {
        res.status(400).json({ error: "roomId, userId and clanId are required" });
        return;
      }

      if (!clanRole || !["OWNER", "ADMIN"].includes(clanRole)) {
        res.status(403).json({ error: "Only clan owners and admins can remove participants" });
        return;
      }

      const removed = await this.liveKitService.removeParticipant(
        this.getClanRoomId(clanId, roomId),
        targetUserId
      );
      if (!removed) {
        res.status(404).json({ error: "Participant is not in this voice channel" });
        return;
      }

      res.status(200).json({ message: "Participant removed from voice channel" });
    } catch (err) {
      next(err);
    }
  };

  private getClanRoomId(clanId: string, roomId: string): string {
    return `clan:${clanId.toLowerCase()}:voice:${roomId.toLowerCase()}`;
  }
}
