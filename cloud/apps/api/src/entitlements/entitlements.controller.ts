import { Controller, Get } from '@nestjs/common';
import { CurrentUser, type AuthUser } from '../auth/decorators';
import { EntitlementsService } from './entitlements.service';

@Controller('entitlements')
export class EntitlementsController {
  constructor(private readonly entitlements: EntitlementsService) {}

  @Get('me')
  me(@CurrentUser() user: AuthUser) {
    return this.entitlements.resolve(user.id);
  }
}
