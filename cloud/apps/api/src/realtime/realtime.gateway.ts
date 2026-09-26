import { Logger } from '@nestjs/common';
import { OnGatewayConnection, WebSocketGateway, WebSocketServer } from '@nestjs/websockets';
import type { Namespace, Socket } from 'socket.io';
import type { Announcement } from '@storm/types';
import { TokenService } from '../auth/token.service';

/**
 * Authenticated push channel (socket.io namespace /realtime). Clients send their access token in
 * the handshake (`auth.token`); unauthenticated sockets are disconnected immediately.
 */
@WebSocketGateway({ namespace: '/realtime' })
export class RealtimeGateway implements OnGatewayConnection {
  private readonly logger = new Logger(RealtimeGateway.name);

  @WebSocketServer()
  server!: Namespace;

  constructor(private readonly tokens: TokenService) {}

  async handleConnection(client: Socket): Promise<void> {
    const token = typeof client.handshake.auth?.token === 'string' ? client.handshake.auth.token : null;
    const claims = token ? await this.tokens.verifyAccess(token) : null;
    if (!claims) {
      client.emit('error', { code: 'unauthorized', message: 'Sign in to receive live updates.' });
      client.disconnect(true);
      return;
    }

    await client.join(`user:${claims.sub}`);
    if (claims.role === 'admin' || claims.role === 'support') {
      await client.join('staff');
    }
  }

  notifyEntitlementsChanged(userId: string): void {
    this.server?.to(`user:${userId}`).emit('entitlements.changed', { at: new Date().toISOString() });
  }

  broadcastAnnouncement(announcement: Announcement): void {
    this.server?.emit('announcement', announcement);
    this.logger.log(`Announcement broadcast: ${announcement.id}`);
  }
}
