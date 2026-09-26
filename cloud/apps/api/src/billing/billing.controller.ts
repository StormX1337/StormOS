import { Body, Controller, Headers, HttpCode, HttpStatus, Post, Req, type RawBodyRequest } from '@nestjs/common';
import { SkipThrottle } from '@nestjs/throttler';
import type { Request } from 'express';
import { checkoutSchema } from '@storm/validation';
import { CurrentUser, Public, type AuthUser } from '../auth/decorators';
import { parseInput } from '../common/api-error';
import { BillingService } from './billing.service';

@Controller('billing')
export class BillingController {
  constructor(private readonly billing: BillingService) {}

  @Post('checkout')
  @HttpCode(HttpStatus.OK)
  checkout(@CurrentUser() user: AuthUser, @Body() body: unknown, @Headers('idempotency-key') idempotencyKey?: string) {
    const input = parseInput(checkoutSchema, body);
    const key = idempotencyKey && /^[A-Za-z0-9-]{8,64}$/.test(idempotencyKey) ? idempotencyKey : undefined;
    return this.billing.createCheckout(user, input.tier, input.interval, key);
  }

  @Post('portal')
  @HttpCode(HttpStatus.OK)
  portal(@CurrentUser() user: AuthUser) {
    return this.billing.createPortal(user);
  }

  @Public()
  @SkipThrottle()
  @Post('webhook')
  @HttpCode(HttpStatus.OK)
  webhook(@Req() request: RawBodyRequest<Request>, @Headers('stripe-signature') signature?: string) {
    return this.billing.handleWebhook(request.rawBody, signature);
  }
}
