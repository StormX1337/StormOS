import { ArgumentsHost, Catch, ExceptionFilter, HttpException, HttpStatus, Logger } from '@nestjs/common';
import type { Response } from 'express';
import type { ApiErrorBody } from '@storm/types';
import { ErrorCodes } from './api-error';

const codeForStatus: Record<number, string> = {
  [HttpStatus.BAD_REQUEST]: ErrorCodes.validation,
  [HttpStatus.UNAUTHORIZED]: ErrorCodes.unauthorized,
  [HttpStatus.PAYMENT_REQUIRED]: ErrorCodes.entitlement,
  [HttpStatus.FORBIDDEN]: ErrorCodes.forbidden,
  [HttpStatus.NOT_FOUND]: ErrorCodes.notFound,
  [HttpStatus.CONFLICT]: ErrorCodes.conflict,
  [HttpStatus.PAYLOAD_TOO_LARGE]: ErrorCodes.validation,
  [HttpStatus.TOO_MANY_REQUESTS]: ErrorCodes.rateLimited,
  [HttpStatus.SERVICE_UNAVAILABLE]: ErrorCodes.unavailable,
};

/** Every error leaves the API as { statusCode, code, message }; internals are logged, never returned. */
@Catch()
export class HttpExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger('Http');

  catch(exception: unknown, host: ArgumentsHost): void {
    const response = host.switchToHttp().getResponse<Response>();
    let body: ApiErrorBody;
    if (exception instanceof HttpException) {
      const status = exception.getStatus();
      const raw = exception.getResponse();
      if (typeof raw === 'object' && raw !== null && 'code' in raw && 'message' in raw) {
        body = raw as ApiErrorBody;
      } else {
        const message = status === HttpStatus.TOO_MANY_REQUESTS
          ? 'Too many requests. Please wait a moment and try again.'
          : typeof raw === 'string'
            ? raw
            : Array.isArray((raw as { message?: unknown }).message)
              ? String((raw as { message: unknown[] }).message[0])
              : String((raw as { message?: unknown }).message ?? exception.message);
        body = { statusCode: status, code: codeForStatus[status] ?? ErrorCodes.internal, message };
      }
    } else {
      this.logger.error(exception instanceof Error ? exception.stack ?? exception.message : String(exception));
      body = { statusCode: HttpStatus.INTERNAL_SERVER_ERROR, code: ErrorCodes.internal, message: 'Something went wrong on our side. Please try again later.' };
    }

    if (response.headersSent) {
      return;
    }

    response.status(body.statusCode).json(body);
  }
}
