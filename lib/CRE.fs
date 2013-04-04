﻿module CRE

open Bytes
open TLSConstants
open TLSInfo
open TLSPRF

// internal
let prfMS sinfo pmsBytes: PRF.masterSecret =
    let pv = sinfo.protocol_version in
    let cs = sinfo.cipher_suite in
    let data = csrands sinfo in
    let res = prf pv cs pmsBytes tls_master_secret data 48 in
    PRF.coerce sinfo res

type rsarepr = bytes
type rsaseed = {seed: rsarepr}
type dhpms = {dhpms: DHGroup.elt}

#if ideal
type pms = RSA_pms of rsaseed | DHE_pms of dhpms
#endif

// We maintain two logs:
// - a log of honest pms values
(* CF
   We need a predicate 'HonestRSAPMS', and its ideal boolean function `honest' 

   To ideally avoid collisions concerns between Honest and Coerced pms, 
   we could discard this log, and use instead a sum type of rsapms, e.g.
   type  rsapms = 
   | IdealRSAPMS    of abstract_seed 
   | ConcreteRSAPMS of rsarepr

MK the first log is used in two idealization steps
*) 

type rsapms = 
#if ideal 
  | IdealRSAPMS of rsaseed 
#endif
  | ConcreteRSAPMS of rsarepr

#if ideal
let honestRSAPMS pk pv pms = 
  match pms with 
  | IdealRSAPMS(s)    -> true
  | ConcreteRSAPMS(s) -> false 

// - a log for looking up good ms values using their pms values

//MK causes problems so rewritten in terms of honest
//MK let corrupt pms = not(honest pms)

let rsalog = ref []
#endif

let genRSA (pk:RSAKey.pk) (vc:TLSConstants.ProtocolVersion) : rsapms = 
    let verBytes = TLSConstants.versionBytes vc in
    let rnd = random 46 in
    let pms = verBytes @| rnd in
    #if ideal
      if RSAKey.honest pk then 
        IdealRSAPMS({seed=pms}) 
      else 
    #endif
        ConcreteRSAPMS(pms)  

let coerceRSA (pk:RSAKey.pk) (pv:ProtocolVersion) b = ConcreteRSAPMS(b)
let leakRSA (pk:RSAKey.pk) (pv:ProtocolVersion) pms = 
  match pms with 
  #if ideal
  | IdealRSAPMS(_) -> Error.unreachable "pms is dishonest" 
  #endif
  | ConcreteRSAPMS(b) -> b 



(* MK assumption notes

We require prfMS to be a deterministic computational randomness extractor for both 
of the distributions generated by genRSA and sampleDH. It is sufficient for the following two 
distributions to be indistinguishable to establish using a standard hybrid argument
indistinguishability of any polynomial length sequence of the two distributions on the right 
from the same length sequence of PRF.sample.

PRF.sample si ~_C prfMS si genRSA pk vc //relate si and pk vc
PRF.sample si ~_C prfMS si sampleDH p g //relate si and p g

*)

#if ideal
let assoc i ms = failwith "todo" 
#endif

let prfSmoothRSA si (pv:ProtocolVersion) pms = 
  match pms with
  #if ideal 
  | IdealRSAPMS(s) ->
        let pk = Cert.get_chain_public_encryption_key si.serverID
        (* CF we assoc on pk and pv, implicitly relying on the absence of collisions between ideal RSAPMSs.*)
        match assoc (pk,pv,pms,csrands si) !rsalog with 
        | Some(ms) -> ms
        | None -> 
                 let ms=PRF.sample si 
                 rsalog := ((pk,pv,pms,csrands si),ms)::!rsalog
                 ms 
  #endif  
  | ConcreteRSAPMS(s) -> prfMS si s



  

#if ideal
let honest_log = ref []
#endif

let sampleDH p g (gx:DHGroup.elt) (gy:DHGroup.elt) = 
    let gz = DHGroup.genElement p g in
    let pms = {dhpms = gz}
    #if ideal
    honest_log := DHE_pms(pms)::!honest_log
    #endif
    pms

let coerceDH (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) b = {dhpms = b} 

let prfSmoothDHE si (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms) =
    //#begin-ideal 
    #if ideal
    if honest(DHE_pms(pms))
    then match tryFind (fun (el1,el2,_) -> el1 = csrands si && el2 = DHE_pms(pms)) !log  with
             Some(_,_,ms) -> ms
           | None -> 
                 let ms=PRF.sample si 
                 log := (csrands si, DHE_pms(pms),ms)::!log;
                 ms 
    else prfMS si pms.dhpms
    //#end-ideal
    #else
    prfMS si pms.dhpms
    #endif


