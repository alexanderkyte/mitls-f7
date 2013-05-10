﻿module MAC

open Bytes
open TLSConstants

open TLSInfo
open Error
open TLSError

type text = bytes
type tag = bytes
type keyrepr = bytes
type key = 
  | KeyNoAuth  of keyrepr
  | Key_SHA256 of MAC_SHA256.key
  | Key_SHA1   of MAC_SHA1.key
 
// We comment out an ideal variant that directly specifies that MAC 
// is ideal at Auth indexes; we do not need that assumption anymore, 
// as we now typecheck this module against plain INT-CMA MAC interfaces:
// idealization now occurs within each of their implementations.
// 
// #if ideal
// type entry = id * text * tag
// let log:entry list ref=ref []
// let rec tmem (e:id) (t:text) (xs: entry list) = 
//  match xs with
//      [] -> false
//    | (e',t',tag)::res when e = e' && t = t' -> true
//    | (e',t',tag)::res -> tmem e t res
// #endif

let Mac (ki:id) key data =
    let a = macAlg_of_id ki in
    // // Commented out old ideal specification:
    // #if ideal
    // let tag = 
    // #endif
      match key with 
        | KeyNoAuth(k)  -> HMAC.MAC a k data 
        | Key_SHA256(k) -> MAC_SHA256.Mac ki k data 
        | Key_SHA1(k)   -> MAC_SHA1.Mac ki k data 
    // #if ideal
    // // We log every authenticated texts, with their index and resulting tag
    // log := (ki, data, tag)::!log;
    // tag
    // #endif

let Verify ki key data tag =
    let a = macAlg_of_id ki in
    match key with 
    | Key_SHA256(k) -> MAC_SHA256.Verify ki k data tag
    | Key_SHA1(k)   -> MAC_SHA1.Verify ki k data tag
    | KeyNoAuth(k)  -> HMAC.MACVERIFY a k data tag
    // #if ideal 
    // // At safe indexes, we use the log to detect and correct verification errors
    // && if authId ki
    //   then 
    //       tmem ki data !log
    //   else 
    //       true  
    // #endif

let GEN ki =
    let a = macAlg_of_id ki in
    #if ideal
    // ideally, we separately keep track of "Auth" keys, 
    // with an additional indirection to HMAFC  
    if authId ki then 
      match a with 
      | a when a = MAC_SHA256.a -> Key_SHA256(MAC_SHA256.GEN ki)
      | a when a = MAC_SHA1.a   -> Key_SHA1(MAC_SHA1.GEN ki)
      | a                       -> unreachable "only strong algorithms provide safety"
    else                   
    #endif
    KeyNoAuth(Nonce.random (macKeySize a))

let COERCE (ki:id) k = KeyNoAuth(k)  
let LEAK (ki:id) k = 
    match k with 
    | Key_SHA256(k) -> unreachable "since we have Auth"
    | Key_SHA1(k)   -> unreachable "since we have Auth"
    | KeyNoAuth(k)  -> k
