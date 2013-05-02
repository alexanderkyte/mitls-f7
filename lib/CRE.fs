﻿module CRE

open Bytes
open TLSConstants
open TLSInfo
open TLSPRF
open Error
open TLSError
open PMS

(*  extractMS is internal and extracts entropy from both rsapms and dhpms bytes 

    Intuitively (and informally) we require extractMS to be a computational randomness extractor for both 
    of the distributions generated by genRSA and sampleDH, i.e. we require that 

    PRF.sample si ~_C extractMS si genRSA pk cv         //for related si and pk cv
    PRF.sample si ~_C extractMS si sampleDH p g gx gy   //for related si and p g gx gy

    In reality honest clients and servers can be tricked by an active adversary to run different 
    and multiple extraction algorithms on the same pms and related client server randomness. 
    We call this an agile extractor, following:
    http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.187.6929&rep=rep1&type=pdf

    si contains potentially random values csrands generated by the client and the server. 
    If we do not want to rely on these values being random we require deterministic extraction.

    If we use them as seeds there may be some relations to non-malleable extractors:
    https://www.cs.nyu.edu/~dodis/ps/it-aka.pdf,
    we are however in the computational setting.   
 *)

let extractMS sinfo pms pmsBytes : PRF.masterSecret =
    let pv = sinfo.protocol_version in
    let cs = sinfo.cipher_suite in
    let data = csrands sinfo in
    let res: PRF.repr = extract (pv,cs) pmsBytes data 48 in
    PRF.coerce sinfo pms res


(*  Idealization strategy: to guarantee that in ideal world mastersecrets (ms) are completely random
    extractRSA samples a ms at radom when called first on arguments pk,cv,pms,csrands si, prfAlg si. 
    
    When called on the same values again, the corresponding master secret is retrieved from a secret log. 

    This is done only for pms for which honestRSAPMS is true and (prfAlg si) is strong.
    Note that in this way many idealized master secrets can be derived from the same pms.
 *)


#if ideal
//MK: no longer needed let todo s = failwith s 

// We maintain a log for looking up good ms values using their msIndex
type rsaentry = RSAKey.pk * ProtocolVersion * rsapms * bytes * prfAlg * PRF.ms

let rsalog = ref []

let rec rsaassoc (pk:RSAKey.pk) (cv:ProtocolVersion) (pms:rsapms)  (csr:bytes) (pa:prfAlg) (mss:rsaentry list): PRF.ms option = 
    match mss with 
    | [] -> None 
    | (pk',cv',pms',csr',pa', ms)::mss' when pk=pk' && cv=cv' && pms=pms' && csr=csr' && pa=pa' -> Some(ms) 
    | _::mss' -> rsaassoc pk cv pms csr pa mss'

#else
//MK: let todo s = ()
#endif

(*private*) 
let accessRSAPMS (pk:RSAKey.pk) (cv:ProtocolVersion) pms = 
  match pms with 
  #if ideal
  | IdealRSAPMS(s) -> s.seed
  #endif
  | ConcreteRSAPMS(b) -> b 

let extractRSA si (cv:ProtocolVersion) pms: PRF.masterSecret = 
    let pk = 
        match Cert.get_chain_public_encryption_key si.serverID with 
        | Correct(pk) -> pk
        | _           -> unexpected "server must have an ID"    
    #if ideal
    (* MK: the following should be made consistent with safeMS_SI
    let i = PRF.msi si (RSAPMS(pk,cv,pms))
    if PRF.safeMS_msIndex i then *)
    if safeMS_SI si then
        //We assoc on pk, cv, pms,  csrands, and prfAlg
        match rsaassoc pk cv pms (csrands si) (PRF.prfAlg si) !rsalog with 
        | Some(ms) -> PRF.masterSecret si (PRF.msi si (RSAPMS(pk,cv,pms))) ms
        | None -> 
                 let (i,ms) = PRF.sample si (RSAPMS(pk,cv,pms))
                 rsalog := (pk,cv,pms,csrands si, PRF.prfAlg si, ms)::!rsalog;
                 PRF.masterSecret si i ms
    else
        extractMS si (RSAPMS(pk, cv, pms)) (accessRSAPMS pk cv pms)
    #else
    extractMS si (RSAPMS(pk, cv, pms)) (accessRSAPMS pk cv pms)
    #endif
(* MK: does not support bad algorithms in agility yet
let extractRSA si (cv:ProtocolVersion) pms = 
  let pk = 
    match (Cert.get_chain_public_encryption_key si.serverID) with 
    | Correct(pk) -> pk
    | _           -> unexpected "server must have an ID"    
  match pms with
  #if ideal
  | IdealRSAPMS(s) (* TODO: when StrongCRE(prfAlg si); otherwise extractMS si s *) ->

        //We assoc on pk, cv, pms and csrands 
        match rsaassoc pk cv pms (csrands si) (PRF.prfAlg si) !rsalog with 
        | Some(ms) -> PRF.msi si (RSAPMS(pk,cv,pms)),ms
        | None -> 
                 let i,ms = PRF.sample si (RSAPMS(pk,cv,pms))
                 rsalog := (pk,cv,pms,csrands si, PRF.prfAlg si, ms)::!rsalog
                 i, ms
  #endif  
  | ConcreteRSAPMS(s) -> todo "SafeMS_SI ==> HonestRSAPMS"; extractMS si (RSAPMS(pk,cv,pms)) s
*)

// The trusted setup for Diffie-Hellman computations
open DHGroup

#if ideal
// We maintain a log for looking up good ms values using their pms values
type dhentry = p * g * elt * elt * dhpms * csrands * prfAlg * PRF.ms
let dhlog = ref []
#endif

(*
let sampleDH p g (gx:DHGroup.elt) (gy:DHGroup.elt) = 
    let gz = DHGroup.genElement p g in
    #if ideal
    IdealDHPMS({seed=gz}) 
    #else
    ConcreteDHPMS(gz)  
    #endif

let coerceDH (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) b = ConcreteDHPMS(b) 
*)
#if ideal

let rec dhassoc (p:p) (g:g) (gx:elt) (gy:elt) (pms:dhpms)  (csr:csrands) (pa:prfAlg) (mss:dhentry list): PRF.ms option = 
    match mss with 
    | [] -> None 
    | (p',g',gx',gy',pms',csr',pa',ms)::mss' when p=p' && g=g' && gx=gx' && gy=gy' && pms=pms' && csr=csr' && pa'=pa-> Some(ms) 
    | _::mss' -> dhassoc p g gx gy pms csr pa mss'

#endif

(* MK does not support bad algorithms in agility yet 
let extractDHE (si:SessionInfo) (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms): PRF.masterSecret =
    match pms with
    //#begin-ideal 
    #if ideal
    | IdealDHPMS(s) -> 
        match dhassoc p g gx gy pms (csrands si) (PRF.prfAlg si) !dhlog with
           | Some(ms) -> (PRF.masterSecret si (PRF.msi si (DHPMS(p,g,gx,gy,pms))) ms)
           | None -> 
                 let i,ms=PRF.sample si (DHPMS(p,g,gx,gy,pms))
                 dhlog := (p, g, gx, gy, pms, csrands si, PRF.prfAlg si, ms)::!dhlog;
                 i,ms 
    #endif
    //#end-ideal
    | ConcreteDHPMS(s) -> todo "SafeHS_SI ==> HonestDHPMS"; extractMS si (DHPMS(p,g,gx,gy,pms)) s
*)

(*private*) 
let accessDHPMS (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms) = 
  match pms with 
  #if ideal
  | IdealDHPMS(b) -> b.seed
  #endif
  | ConcreteDHPMS(b) -> b 

   
let extractDHE (si:SessionInfo) (p:DHGroup.p) (g:DHGroup.g) (gx:DHGroup.elt) (gy:DHGroup.elt) (pms:dhpms): PRF.masterSecret =  
    #if ideal
    if safeMS_SI si then
        //We assoc on pk, cv, pms,  csrands, and prfAlg
         match dhassoc p g gx gy pms (csrands si) (PRF.prfAlg si) !dhlog with
           | Some(ms) -> (PRF.masterSecret si (PRF.msi si (DHPMS(p,g,gx,gy,pms))) ms)
           | None -> 
                 let i,ms=PRF.sample si (DHPMS(p,g,gx,gy,pms))
                 dhlog := (p, g, gx, gy, pms, csrands si, PRF.prfAlg si, ms)::!dhlog;
                 PRF.masterSecret si i ms
    else
        extractMS si (DHPMS(p,g,gx,gy,pms)) (accessDHPMS p g gx gy pms)
    #else
    extractMS si (DHPMS(p,g,gx,gy,pms)) (accessDHPMS p g gx gy pms)
    #endif
