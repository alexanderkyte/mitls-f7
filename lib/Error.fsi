﻿module Error

type alertDescription = 
    | AD_close_notify
    | AD_unexpected_message
    | AD_bad_record_mac
    | AD_decryption_failed
    | AD_record_overflow
    | AD_decompression_failure
    | AD_handshake_failure
    | AD_no_certificate
    | AD_bad_certificate_warning
    | AD_bad_certificate_fatal
    | AD_unsupported_certificate_warning
    | AD_unsupported_certificate_fatal
    | AD_certificate_revoked_warning
    | AD_certificate_revoked_fatal
    | AD_certificate_expired_warning
    | AD_certificate_expired_fatal
    | AD_certificate_unknown_warning
    | AD_certificate_unknown_fatal
    | AD_illegal_parameter
    | AD_unknown_ca
    | AD_access_denied
    | AD_decode_error
    | AD_decrypt_error
    | AD_export_restriction
    | AD_protocol_version
    | AD_insufficient_security
    | AD_internal_error
    | AD_user_cancelled_warning
    | AD_user_cancelled_fatal
    | AD_no_renegotiation
    | AD_unsupported_extension

type ErrorCause =
    | Tcp
    | MAC
    | Hash
    | Parsing
    | Encryption
    | Protocol
    | Record
    | RecordPadding
    | RecordFragmentation
    | RecordCompression
    | RecordVersion
    | AlertAlreadySent
    | AlertProto
    | HSError of alertDescription
    | CertificateParsing
    | Dispatcher
    | TLS

type ErrorKind =
    | Unsupported
    | CheckFailed
    | WrongInputParameters
    | InvalidState
    | Internal
    | UserAborted
    | HSSendAlert
    | ConnectionClosed

type 'a Result =
    | Error of ErrorCause * ErrorKind
    | Correct of 'a

val correct: 'a -> 'a Result
val unexpectedError: string -> 'a