import { Router } from "express";
import { VoiceController } from "../controllers/VoiceController";
import { WebhookController } from "../controllers/WebhookController";
import { ILiveKitService } from "../services/LiveKitService";
import { DmAuthorizationService } from "../services/DmAuthorizationService";
import { jwtAuth } from "../middlewares/jwtAuth";

export function createVoiceRouter(liveKitService: ILiveKitService): Router {
  const router = Router();

  const dmAuthorizationService = new DmAuthorizationService();
  const voiceController = new VoiceController(liveKitService, dmAuthorizationService);
  const webhookController = new WebhookController();

  // Token alma endpoint'i
  router.get(
    "/join-room/:roomId/clanId/:clanId",
    jwtAuth,
    voiceController.joinRoom
  );
  router.get("/join-room/:roomId", jwtAuth, voiceController.joinRoom);
  router.delete(
    "/rooms/:roomId/participants/:userId/clanId/:clanId",
    jwtAuth,
    voiceController.kickParticipant
  );

  // LiveKit webhook endpoint'i
  router.post("/webhook", webhookController.handleWebhook);

  return router;
}
