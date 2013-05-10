﻿module HMAC

open Bytes
open TLSConstants

type key = bytes
type data = bytes
type mac = bytes

val MAC:       macAlg -> key -> data -> mac
val MACVERIFY: macAlg -> key -> data -> mac -> bool

(* SSL/TLS Constants *)

val ssl_pad1_md5: bytes
val ssl_pad2_md5: bytes
val ssl_pad1_sha1: bytes
val ssl_pad2_sha1: bytes
