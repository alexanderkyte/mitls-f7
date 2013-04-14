﻿module CRE

open Bytes
open TLSConstants
open TLSInfo
open TLSPRF
open Error
open DHGroup

// internal
let prfMS sinfo pmsBytes: PRF.masterSecret =
    let pv = sinfo.protocol_version in
    let cs = sinfo.cipher_suite in
    let data = csrands sinfo in
    let res = prf pv cs pmsBytes tls_master_secret data 48 in
    PRF.coerce sinfo res

type rsarepr = bytes
type rsaseed = {seed: rsarepr}

type rsapms = 
#if ideal 
  | IdealRSAPMS of rsaseed 
#endif
  | ConcreteRSAPMS of rsarepr

#if ideal
let honestRSAPMS (pk:RSAKey.pk) (pv:TLSConstants.ProtocolVersion) pms = 
  match pms with 
  | IdealRSAPMS(s)    -> true
  | ConcreteRSAPMS(s) -> false
  
type rsapreds = 
    | EncryptedRSAPMS of RSAKey.pk * ProtocolVersion * rsapms * bytes
 

// We maintain a log for looking up good ms values using their pms values
type rsaentry = (RSAKey.pk * ProtocolVersion * rsapms * bytes * SessionInfo) * PRF.masterSecret

let rsalog = ref []

//CF temporary counter-example!
let rsaassoc0 (si:SessionInfo) (mss:((SessionInfo * PRF.masterSecret) list)) : PRF.masterSecret option = 
    match mss with 
    | [] -> None 
    | (si',ms)::mss' -> Some(ms) 
  
let rec rsaassoc (i:(RSAKey.pk * ProtocolVersion * rsapms * bytes * SessionInfo)) (mss:rsaentry list): PRF.masterSecret option = 
    let pk,pv,pms,csr,si=i in
    match mss with 
    | [] -> None 
    | ((pk',pv',pms',csr',si'),ms)::mss' when pk=pk' && pv=pv' && pms=pms' && csr=csr' -> Some(ms) 
    | _::mss' -> rsaassoc i mss'

#endif

let genRSA (pk:RSAKey.pk) (vc:ProtocolVersion): rsapms = 
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
  | IdealRSAPMS(_) -> Error.unexpected "pms is dishonest" //MK changed to unexpected from unreachable
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
let todo s = failwith s 
#else
let todo s = ()
#endif

let prfSmoothRSA si (pv:ProtocolVersion) pms = 
  match pms with
  #if ideal 
  | IdealRSAPMS(s) ->
        let pk = 
            match (Cert.get_chain_public_encryption_key si.serverID) with 
            | Correct(pk) -> pk
            | _           -> unexpected "server must have an ID"    
        (* CF we assoc on pk and pv, implicitly relying on the absence of collisions between ideal RSAPMSs.*)
        match rsaassoc (pk,pv,pms,csrands si,si) !rsalog with 
        | Some(ms) -> ms
        | None -> 
                 let ms=PRF.sample si 
                 rsalog := ((pk,pv,pms,csrands si,si),ms)::!rsalog
                 ms 
  #endif  
  | ConcreteRSAPMS(s) -> todo "SafeHS_SI ==> HonestRSAPMS"; prfMS si s



type dhrepr = bytes
type dhseed = {seed: dhrepr}

(* CF
   We need a predicate 'HonestDHPMS', and its ideal boolean function `honestDHPMS' 

   To ideally avoid collisions concerns between Honest and Coerced pms, 
   we could discard this log, and use instead a sum type of rsapms, e.g.
   type  rsapms = 
   | IdealDHPMS    of dhseed 
   | ConcreteDHPMS of dhrepr

MK honestDHPMS is used in two idealization steps
*) 

type dhpms = 
#if ideal 
  | IdealDHPMS of dhseed 
#endif
  | ConcreteDHPMS of dhrepr

#if ideal
let honestDHPMS (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) pms = 
  match pms with 
  | IdealDHPMS(s)    -> true
  | ConcreteDHPMS(s) -> false 

// We maintain a log for looking up good ms values using their pms values

type dhentry = (p * g * elt * elt * dhpms * bytes * SessionInfo) * PRF.masterSecret

let dhlog = ref []

#endif

let sampleDH p g (gx:DHGroup.elt) (gy:DHGroup.elt) = 
    let gz = DHGroup.genElement p g in
    #if ideal
    IdealDHPMS({seed=gz}) 
    #else
    ConcreteDHPMS(gz)  
    #endif

let coerceDH (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) b = ConcreteDHPMS(b) 

#if ideal

let rec dhassoc (i:(p * g * elt * elt * dhpms * bytes * SessionInfo)) (mss:dhentry list): PRF.masterSecret option = 
    let (p,g,gx,gy,pms,csr,_)=i in
    match mss with 
    | [] -> None 
    | ((p',g',gx',gy',pms',csr',_),ms)::mss' when p=p' && g=g' && gx=gx' && gy=gy' && pms=pms' && csr=csr' -> Some(ms) 
    | _::mss' -> dhassoc i mss'

#endif

let prfSmoothDHE (si:SessionInfo) (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms): PRF.masterSecret =
    match pms with
    //#begin-ideal 
    #if ideal
    | IdealDHPMS(s) -> 
        match dhassoc (p, g, gx, gy, pms, csrands si, si) !dhlog with
           | Some(ms) -> ms
           | None -> 
                 let ms=PRF.sample si 
                 dhlog := ((p, g, gx, gy, pms, csrands si, si), ms)::!dhlog;
                 ms 
    #endif
    //#end-ideal
    | ConcreteDHPMS(s) -> todo "SafeHS_SI ==> HonestDHPMS"; prfMS si s
   