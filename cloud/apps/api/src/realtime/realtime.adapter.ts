import type { INestApplicationContext } from '@nestjs/common';
import { IoAdapter } from '@nestjs/platform-socket.io';
import type { ServerOptions } from 'socket.io';

/** socket.io adapter restricted to the configured web/admin origins. */
export class RealtimeAdapter extends IoAdapter {
  constructor(app: INestApplicationContext, private readonly origins: readonly string[]) {
    super(app);
  }

  override createIOServer(port: number, options?: ServerOptions): unknown {
    return super.createIOServer(port, { ...options, cors: { origin: [...this.origins], credentials: false }, maxHttpBufferSize: 64 * 1024 });
  }
}
