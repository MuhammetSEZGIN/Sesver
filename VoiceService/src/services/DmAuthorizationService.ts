import config from "../config";

export interface IDmAuthorizationService {
  isParticipant(conversationId: string, userId: string): Promise<boolean>;
}

const DM_ROOM_PREFIX = "dm-";

export function isDmRoomId(roomId: string): boolean {
  return roomId.startsWith(DM_ROOM_PREFIX);
}

export function extractConversationId(roomId: string): string {
  return roomId.slice(DM_ROOM_PREFIX.length);
}

export class DmAuthorizationService implements IDmAuthorizationService {
  async isParticipant(conversationId: string, userId: string): Promise<boolean> {
    const url = `${config.messageServiceUrl}/api/Dm/conversations/${encodeURIComponent(conversationId)}/is-participant`;

    const response = await fetch(url, {
      method: "GET",
      headers: { "X-User-Id": userId },
    });

    return response.ok;
  }
}
