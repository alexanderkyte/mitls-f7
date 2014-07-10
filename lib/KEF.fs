﻿module KEF

open Bytes
open TLSConstants
open TLSInfo
open DHGroup // The trusted setup for Diffie-Hellman computations
open PMS

(* extractMS is internal and extracts entropy from both rsapms and dhpms bytes 

   Intuitively (and informally) we require extractMS to be a computational 
   randomness extractor for both of the distributions generated by genRSA and 
   sampleDH, i.e. we require that 

   PRF.sample si ~_C extractMS si genRSA pk cv         //for related si and pk cv
   PRF.sample si ~_C extractMS si sampleDH p g gx gy   //for related si and p g gx gy

   In reality honest clients and servers can be tricked by an active adversary 
   to run different and multiple extraction algorithms on the same pms and 
   related client server randomness. We call this an agile extractor, following:
   http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.187.6929&rep=rep1&type=pdf

   si contains potentially random values csrands generated by the client and the server. 
   If we do not want to rely on these values being random we require deterministic extraction.

   If we use them as seeds there may be some relations to non-malleable extractors:
   https://www.cs.nyu.edu/~dodis/ps/it-aka.pdf,
   we are however in the computational setting.   
 *)

let private extractMS si pmsBytes : PRF.masterSecret =
    let cs = si.cipher_suite in
    let data = csrands si in
    let ca = kefAlg si in
    let res = TLSPRF.extract ca pmsBytes data 48 in
    let i = msi si
    PRF.coerce i res


let private accessRSAPMS (pk:RSAKey.pk) (cv:ProtocolVersion) pms = 
  match pms with 
  #if ideal
  | IdealRSAPMS(s) -> s.seed
  #endif
  | ConcreteRSAPMS(b) -> b 

let private accessDHPMS (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms) = 
  match pms with 
  #if ideal
  | IdealDHPMS(b) -> b.seed
  #endif
  | ConcreteDHPMS(b) -> b 

let private accessPMS (pms:PMS.pms) =
  match pms with
  | PMS.RSAPMS(pk,cv,rsapms) ->  accessRSAPMS pk cv rsapms
  | PMS.DHPMS(p,g,gx,gy,dhpms) -> accessDHPMS p g gx gy dhpms

#if ideal
// We maintain a log for looking up good ms values using their msId
type entry = msId * PRF.ms
let log = ref []
let rec assoc (i:msId) entries: option<PRF.ms> = 
    match entries with 
    | []                      -> None 
    | (i', ms)::entries when i = i' -> Some(ms) 
    | _::entries              -> assoc i entries
#endif

(* Idealization strategy: to guarantee that in ideal world 
   mastersecrets (ms) are completely random extract samples 
   a ms at random when called first on arguments such that 
   'msi si' is not yet in the log. 
    
   When called on arguments that result in the same values 
   again, the corresponding master secret is retrieved from 
   a secret log. 

   This is done only for pms for which safeCRE si is true.
   Note that in this way many idealized master secrets can 
   be derived from the same pms. *)


let extract si pms: PRF.masterSecret = 
    #if ideal
    if safeCRE si then
        let i = msi si 
        match assoc i !log with 
        | Some(ms) -> ms
        | None -> 
                let ms = PRF.sample i
                log := (i, ms)::!log;
                ms            
    else
    #endif
        extractMS si (accessPMS pms)

//MK unused? type log = bytes

let private extractMS_extended si pmsBytes : PRF.masterSecret =
    let ca = kefAlg_extended si in
    let sh = si.session_hash in
    let res = TLSPRF.extract ca pmsBytes sh 48 in
    let i = msi si
    PRF.coerce i res

let extract_extended si pms =
    #if ideal
    if safeCRE si then
        let i = msi si 
        match assoc i !log with 
        | Some(ms) -> ms
        | None -> 
                let ms = PRF.sample i
                log := (i, ms)::!log;
                ms            
    else
    #endif
        extractMS_extended si (accessPMS pms)
