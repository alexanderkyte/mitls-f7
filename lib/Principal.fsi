﻿module Principal

open Data
open Error_handling
open Crypto

type pri_cert

val certificate_of_bytes: bytes -> pri_cert Result
val bytes_of_certificate: pri_cert -> bytes

val pubKey_of_certificate: pri_cert -> key
val priKey_of_certificate: pri_cert -> key

val certificate_has_signing_capability: pri_cert -> bool
val certificate_is_dsa: pri_cert -> bool