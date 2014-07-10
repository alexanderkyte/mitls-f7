﻿module Cert

open Bytes
open Error
open TLSError
open UntrustedCert

type hint = UntrustedCert.hint 
type cert = UntrustedCert.cert  

type chain = UntrustedCert.chain
type sign_cert = (chain * Sig.alg * Sig.skey) option
type enc_cert  = (chain * RSAKey.sk) option

val for_signing : list<Sig.alg> -> hint -> list<Sig.alg> -> sign_cert
val for_key_encryption : list<Sig.alg> -> hint -> enc_cert

val get_public_signing_key : cert -> Sig.alg -> Result<Sig.pkey>
val get_public_encryption_key : cert -> Result<RSAKey.pk>

val get_chain_public_signing_key : chain -> Sig.alg -> Result<Sig.pkey>
val get_chain_public_encryption_key : chain -> Result<RSAKey.pk>

val is_chain_for_signing : chain -> bool
val is_chain_for_key_encryption : chain -> bool

val get_hint : chain -> hint option
val validate_cert_chain : list<Sig.alg> -> chain -> bool
val parseCertificateList: bytes -> chain -> Result<chain>
val certificateListBytes: chain -> bytes
