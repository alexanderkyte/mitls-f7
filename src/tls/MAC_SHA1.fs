﻿(* Copyright (C) 2012--2014 Microsoft Research and INRIA *)

#light "off"

module MAC_SHA1

open Bytes
open TLSConstants
open TLSInfo
open Error
open TLSError

type text = bytes
type tag = bytes
type keyrepr = bytes
type key = {k:keyrepr}

// for concreteness; the rest of the module is parametric in a 
let a = MA_HMAC(SHA) 

#if ideal 
// We maintain a table of MACed plaintexts
type entry = id * text * tag
let log:ref<list<entry>> =ref []
let rec tmem (e:id) (t:text) (xs: list<entry>) = 
  match xs with
      [] -> false
    | (e',t',m)::res when e = e' && t = t' -> true
    | (e',t',m)::res -> tmem e t res
#endif

let GEN (ki:id) = {k= Nonce.random (macKeySize(a))}

let Mac (ki:id) key t =
    let m = HMAC.tls_mac a key.k t in
    #if ideal 
    // We log every authenticated texts, with their index and resulting tag
    log := (ki, t, m)::!log;
    #endif
    m

let Verify (ki:id) key t m =
    HMAC.tls_macVerify a key.k t m
    #if ideal 
    // We use the log to correct any verification errors
    && tmem ki t !log
    #endif
