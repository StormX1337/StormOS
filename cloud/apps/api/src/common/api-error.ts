import { HttpException, HttpStatus } from '@nestjs/common';
import type { ApiErrorBody } from '@storm/types';
import { describeZodError, type z } from '@storm/validation';

export const ErrorCodes = {
  validation: 'validation_failed',
  unauthorized: 'unauthorized',
  forbidden: 'forbidden',
  notFound: 'not_found',
  conflict: 'conflict',
  entitlement: 'entitlement_required',
  rateLimited: 'rate_limited',
  unavailable: 'service_unavailable',
  internal: 'internal',
} as const;

/** An HTTP error with a stable code and a user-safe message. */
export class ApiError extends HttpException {
  constructor(status: HttpStatus, code: string, message: string) {
    super({ statusCode: status, code, message } satisfies ApiErrorBody, status);
  }

  static badRequest(message: string): ApiError {
    return new ApiError(HttpStatus.BAD_REQUEST, ErrorCodes.validation, message);
  }

  static unauthorized(message = 'Sign in to continue.'): ApiError {
    return new ApiError(HttpStatus.UNAUTHORIZED, ErrorCodes.unauthorized, message);
  }

  static forbidden(message = 'You do not have permission to do this.'): ApiError {
    return new ApiError(HttpStatus.FORBIDDEN, ErrorCodes.forbidden, message);
  }

  static notFound(message = 'Not found.'): ApiError {
    return new ApiError(HttpStatus.NOT_FOUND, ErrorCodes.notFound, message);
  }

  static conflict(message: string): ApiError {
    return new ApiError(HttpStatus.CONFLICT, ErrorCodes.conflict, message);
  }

  static entitlement(message: string): ApiError {
    return new ApiError(HttpStatus.PAYMENT_REQUIRED, ErrorCodes.entitlement, message);
  }

  static unavailable(message: string): ApiError {
    return new ApiError(HttpStatus.SERVICE_UNAVAILABLE, ErrorCodes.unavailable, message);
  }

  static tooMany(message: string): ApiError {
    return new ApiError(HttpStatus.TOO_MANY_REQUESTS, ErrorCodes.rateLimited, message);
  }
}

/** Parses untrusted input or throws a 400 with a readable message. */
export function parseInput<T extends z.ZodTypeAny>(schema: T, value: unknown): z.infer<T> {
  const result = schema.safeParse(value);
  if (!result.success) {
    throw ApiError.badRequest(describeZodError(result.error));
  }

  return result.data as z.infer<T>;
}
