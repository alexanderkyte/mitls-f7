﻿module Handshake
// State machine begins
open Bytes
open Error
open TLSError
open TLSConstants
open TLSExtensions
open TLSInfo
open Range
open HandshakeMessages

type events = 
    Authorize of Role * SessionInfo
  | Configure of Role * epoch * config
  | EvSentFinishedFirst of ConnectionInfo * bool
  | Negotiated of Role * SessionInfo * config * config
  | CompleteEpoch of Role * epoch // for testing
  | Complete of ConnectionInfo   // for testing
  | LMsg of (Cert.chain * Sig.alg * Sig.skey) option * Sig.text

(* verify data authenticated by the Finished messages *)
type log = bytes         (* message payloads so far, to be eventually authenticated *) 

// The constructor indicates either what we are doing locally or which peer message we are expecting, 

type serverState =  (* note that the CertRequest bits are determined by the config *) 
                    (* we may omit some ProtocolVersion, mostly a ghost variable *)
   | ClientHello                  of cVerifyData * sVerifyData

   | ClientCertificateRSA         of SessionInfo * ProtocolVersion * RSAKey.sk * log
   | ServerCheckingCertificateRSA of SessionInfo * ProtocolVersion * RSAKey.sk * log * Cert.chain * bytes
   | ClientKeyExchangeRSA         of SessionInfo * ProtocolVersion * RSAKey.sk * log

   | ClientCertificateDH          of SessionInfo * log
   | ServerCheckingCertificateDH  of SessionInfo * log * bytes
   | ClientKeyExchangeDH          of SessionInfo * log 

   | ClientCertificateDHE         of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * DH.secret * log
   | ServerCheckingCertificateDHE of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * DH.secret * log * Cert.chain * bytes
   | ClientKeyExchangeDHE         of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * DH.secret * log

   | ClientKeyExchangeDH_anon     of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * DH.secret * log

   | CertificateVerify            of SessionInfo * PRF.masterSecret * log 
   | ClientCCS                    of SessionInfo * PRF.masterSecret * log
   | ClientFinished               of SessionInfo * PRF.masterSecret * epoch * StatefulLHAE.writer * log
   (* by convention, the parameters are named si, cv, cr', sr', ms, log *)
   | ServerWritingCCS             of SessionInfo * PRF.masterSecret * epoch * StatefulLHAE.writer * cVerifyData * log
   | ServerWritingFinished        of SessionInfo * PRF.masterSecret * epoch * cVerifyData * sVerifyData

   | ServerWritingCCSResume       of epoch * StatefulLHAE.writer * epoch * StatefulLHAE.reader * PRF.masterSecret * log
   | ClientCCSResume              of epoch * StatefulLHAE.reader * sVerifyData * PRF.masterSecret * log
   | ClientFinishedResume         of SessionInfo * PRF.masterSecret * epoch * sVerifyData * log

   | ServerIdle                   of cVerifyData * sVerifyData
   (* the ProtocolVersion is the highest TLS version proposed by the client *)

type clientState = 
   | ServerHello                  of crand * sessionID * clientExtension list * cVerifyData * sVerifyData * log

   | ServerCertificateRSA         of SessionInfo * log
   | ClientCheckingCertificateRSA of SessionInfo * log * Cert.cert list * ProtocolVersion option * bytes
   | CertificateRequestRSA        of SessionInfo * log (* In fact, CertReq or SHelloDone will be accepted *)
   | ServerHelloDoneRSA           of SessionInfo * Cert.sign_cert * log

   | ServerCertificateDH          of SessionInfo * log
   | ClientCheckingCertificateDH  of SessionInfo * log * ProtocolVersion option * bytes
   | CertificateRequestDH         of SessionInfo * log (* We pick our cert and store it in sessionInfo as soon as the server requests it.
                                                         We put None if we don't have such a certificate, and we know whether to send
                                                         the Certificate message or not based on the state when we receive the Finished message *)
   | ServerHelloDoneDH            of SessionInfo * log

   | ServerCertificateDHE         of SessionInfo * log
   | ClientCheckingCertificateDHE of SessionInfo * log * ProtocolVersion option * bytes
   | ServerKeyExchangeDHE         of SessionInfo * log
   | CertificateRequestDHE        of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * log
   | ServerHelloDoneDHE           of SessionInfo * Cert.sign_cert * DHGroup.p * DHGroup.g * DHGroup.elt * log

   | ServerKeyExchangeDH_anon of SessionInfo * log (* Not supported yet *)
   | ServerHelloDoneDH_anon of SessionInfo * DHGroup.p * DHGroup.g * DHGroup.elt * log

   | ClientWritingCCS       of SessionInfo * PRF.masterSecret * log
   | ServerCCS              of SessionInfo * PRF.masterSecret * epoch * StatefulLHAE.reader * cVerifyData * log
   | ServerFinished         of SessionInfo * PRF.masterSecret * epoch * cVerifyData * log

   | ServerCCSResume        of epoch * StatefulLHAE.writer * epoch * StatefulLHAE.reader * PRF.masterSecret * log
   | ServerFinishedResume   of epoch * StatefulLHAE.writer * PRF.masterSecret * log
   | ClientWritingCCSResume of epoch * StatefulLHAE.writer * PRF.masterSecret * sVerifyData * log
   | ClientWritingFinishedResume of cVerifyData * sVerifyData

   | ClientIdle             of cVerifyData * sVerifyData

type protoState = // Cannot use Client and Server, otherwise clashes with Role
  | PSClient of clientState
  | PSServer of serverState

let clientState (ci:ConnectionInfo) (p:clientState) = PSClient(p)
let serverState (ci:ConnectionInfo) (p:serverState) = PSServer(p)
 
type hs_state = {
  (* I/O buffers *)
  hs_outgoing    : bytes;                  (* outgoing data *)
  hs_incoming    : bytes;                  (* partial incoming HS message *)
  (* local configuration *)
  poptions: config; 
  sDB: SessionDB.t;
  (* current handshake & session we are establishing *) 
  pstate: protoState;
}

type nextState = hs_state

/// Initiating Handshakes, mostly on the client side. 

let init (role:Role) poptions =
    (* Start a new session without resumption, as the first epoch on this connection. *)
    let sid = empty_bytes in
    let rand = Nonce.mkHelloRandom() in
    match role with
    | Client -> 
        let ci = initConnection role rand in
        Pi.assume (Configure(Client,ci.id_in,poptions));
        let extL = prepareClientExtensions poptions ci empty_bytes None in
        let ext = clientExtensionsBytes extL in
        let cHelloBytes = clientHelloBytes poptions rand sid ext in
        let sdb = SessionDB.create poptions in 
        (ci,{hs_outgoing = cHelloBytes;
                     hs_incoming = empty_bytes;
                     poptions = poptions;
                     sDB = sdb;
                     pstate = PSClient (ServerHello (rand, sid, extL, empty_bytes, empty_bytes, cHelloBytes))
            })
    | Server ->
        let ci = initConnection role rand in
        Pi.assume (Configure(Client,ci.id_in,poptions));
        let sdb = SessionDB.create poptions in 
        (ci,{hs_outgoing = empty_bytes
             hs_incoming = empty_bytes
             poptions = poptions
             sDB = sdb
             pstate = PSServer (ClientHello(empty_bytes,empty_bytes))
            })

let resume next_sid poptions =
    (* Resume a session, as the first epoch on this connection.
       Set up our state as a client. Servers cannot resume *)

    (* Search a client sid in the DB *)
    let sDB = SessionDB.create poptions in
    match SessionDB.select sDB next_sid Client poptions.server_name with
    | None -> init Client poptions
    | Some (retrieved) ->
    let (retrievedSinfo,retrievedMS) = retrieved in
    match retrievedSinfo.sessionID with
    | xx when length xx = 0 -> unexpected "[resume] a resumed session should always have a valid sessionID"
    | sid ->
    let rand = Nonce.mkHelloRandom () in
    let ci = initConnection Client rand in
    Pi.assume (Configure(Server,ci.id_in,poptions));
    let extL = prepareClientExtensions poptions ci empty_bytes (Some(retrievedSinfo.session_hash))
    let ext = clientExtensionsBytes extL
    let cHelloBytes = clientHelloBytes poptions rand sid ext in
    let sdb = SessionDB.create poptions
    (ci,{hs_outgoing = cHelloBytes
         hs_incoming = empty_bytes
         poptions = poptions
         sDB = sdb
         pstate = PSClient (ServerHello (rand, sid, extL, empty_bytes, empty_bytes, cHelloBytes))
        })

let rehandshake (ci:ConnectionInfo) (state:hs_state) (ops:config) =
    (* Start a non-resuming handshake, over an existing epoch.
       Only client side, since a server can only issue a HelloRequest *)
    match state.pstate with
    | PSClient (cstate) ->
        match cstate with
        | ClientIdle(cvd,svd) ->
            let rand = Nonce.mkHelloRandom () in
            let sid = empty_bytes in
            let extL = prepareClientExtensions ops ci cvd None in
            let ext = clientExtensionsBytes extL in
            let cHelloBytes = clientHelloBytes ops rand sid ext in
            Pi.assume (Configure(Client,ci.id_in,ops));
            let sdb = SessionDB.create ops
            (true,{hs_outgoing = cHelloBytes
                   hs_incoming = empty_bytes
                   poptions = ops
                   sDB = sdb
                   pstate = PSClient (ServerHello (rand, sid, extL, cvd,svd, cHelloBytes))
                   })
        | _ -> (* handshake already happening, ignore this request *)
            (false,state)
    | PSServer (_) -> unexpected "[rehandshake] should only be invoked on client side connections."

let rekey (ci:ConnectionInfo) (state:hs_state) (ops:config) : bool * hs_state =
#if avoid
  failwith "unverified for now"
#else
    if isInitEpoch(ci.id_out) then
        unexpected "[rekey] should only be invoked on established connections."
    else
    (* Start a (possibly) resuming handshake over an existing epoch *)
    let si = epochSI(ci.id_out) in // or equivalently ci.id_in
    let sidOp = si.sessionID in
    match sidOp with
    | xx when length xx = 0 -> (* Non resumable session, let's do a full handshake *)
        rehandshake ci state ops
    | sid ->
        let sDB = SessionDB.create ops in
        (* Ensure the sid is in the SessionDB *)
        match SessionDB.select sDB sid Client ops.server_name with
        | None -> (* Maybe session expired, or was never stored. Let's not resume *)
            rehandshake ci state ops
        | Some s ->
            let (retrievedSinfo,retrievedMS) = s
            match state.pstate with
            | PSClient (cstate) ->
                match cstate with
                | ClientIdle(cvd,svd) ->
                    let rand = Nonce.mkHelloRandom () in
                    let extL = prepareClientExtensions ops ci cvd (Some(retrievedSinfo.session_hash))
                    let ext = clientExtensionsBytes extL in
                    Pi.assume (Configure(Client,ci.id_in,ops));
                    let cHelloBytes = clientHelloBytes ops rand sid ext in
                    (true,{hs_outgoing = cHelloBytes
                           hs_incoming = empty_bytes
                           poptions = ops
                           sDB = sDB
                           pstate = PSClient (ServerHello (rand, sid, extL, cvd, svd, cHelloBytes))
                           })
                | _ -> (* Handshake already ongoing, ignore this request *)
                    (false,state)
            | PSServer (_) -> unexpected "[rekey] should only be invoked on client side connections."
#endif 

let request (ci:ConnectionInfo) (state:hs_state) (ops:config) =
    match state.pstate with
    | PSClient _ -> unexpected "[request] should only be invoked on server side connections."
    | PSServer (sstate) ->
        match sstate with
        | ServerIdle(cvd,svd) ->
            let sdb = SessionDB.create ops
            (* Put HelloRequest in outgoing buffer (and do not log it), and move to the ClientHello state (so that we don't send HelloRequest again) *)
            (true, { hs_outgoing = helloRequestBytes
                     hs_incoming = empty_bytes
                     poptions = ops
                     sDB = sdb
                     pstate = PSServer(ClientHello(cvd,svd))
                    })
        | _ -> (* Handshake already ongoing, ignore this request *)
            (false,state)

let getPrincipal ci state =
  match ci.role with
    | Client -> state.poptions.server_name
    | Server -> state.poptions.client_name

let invalidateSession ci state =
  let i = isInitEpoch(ci.id_in) in
    if i = true then
        state
    else
        let si = epochSI(ci.id_in) // FIXME: which epoch to choose? Here it matters since they could be mis-aligned
        match si.sessionID with
        | xx when length xx = 0 -> state
        | sid ->
            let hint = getPrincipal ci state
            let sdb = SessionDB.remove state.sDB sid ci.role hint in
            {state with sDB=sdb}

let getNextEpochs ci si crand srand =
    let id_in  = nextEpoch ci.id_in  crand srand si in
    let id_out = nextEpoch ci.id_out crand srand si in
    {ci with id_in = id_in; id_out = id_out}

type outgoing =
  | OutIdle of nextState
  | OutSome of range * HSFragment.plain * nextState
  | OutCCS of  range * HSFragment.plain (* the unique one-byte CCS *) *
               ConnectionInfo * StatefulLHAE.state * nextState
  | OutFinished of range * HSFragment.plain * nextState
  | OutComplete of range * HSFragment.plain * nextState

let check_negotiation (r:Role) (si:SessionInfo) (c:config) = 
  Pi.assume (Negotiated(r,si,c,c)) (* FIXME: Dummy definition. 
                                      Define valid negotiations *)

let next_fragment ci state =
    match state.hs_outgoing with
    | xx when length xx = 0 ->
        match state.pstate with
        | PSClient(cstate) ->
            match cstate with
            | ClientWritingCCS (si,ms,log) ->
                let next_ci = getNextEpochs ci si si.init_crand si.init_srand in
                let nki_in = id next_ci.id_in in
                let nki_out = id next_ci.id_out in
                let (reader,writer) = PRF.keyGenClient nki_in nki_out ms in
                let cr' = si.init_crand in
                let sr' = si.init_srand in
                Pi.assume (SentCCS(Client,cr',sr',si));
                //CF now passing si, instead of next_ci.id_out
                //CF but the precondition should be on F(si)
                let cvd = PRF.makeVerifyData si ms Client log in
                let cFinished = messageBytes HT_finished cvd in
                let log = log @| cFinished in
                let ki_out = ci.id_out in
                let (rg,f,_) = makeFragment ki_out CCSBytes in
                let ci = {ci with id_out = next_ci.id_out} in 
                (* Should be provable from SentCCS *)
                Pi.assume (CompleteEpoch(ci.role,ci.id_out));
                OutCCS(rg,f,ci,writer,
                       {state with hs_outgoing = cFinished 
                                   pstate = PSClient(ServerCCS(si,ms,next_ci.id_in,reader,cvd,log))})

            | ClientWritingCCSResume(e,w,ms,svd,log) ->
                let cr' = epochCRand(e) in
                let sr' = epochSRand(e) in
                let si' = epochSI(e) in
                Pi.assume (SentCCS(Client,cr',sr',si'));
                let cvd = PRF.makeVerifyData si' ms Client log in
                let cFinished = messageBytes HT_finished cvd in
                let ki_out = ci.id_out in
                let (rg,f,_) = makeFragment ki_out CCSBytes in
                let ci = {ci with id_out = e} in 
                Pi.assume (CompleteEpoch(ci.role,e));
                OutCCS(rg,f,ci,w,
                       {state with hs_outgoing = cFinished
                                   pstate = PSClient(ClientWritingFinishedResume(cvd,svd))})
            | _ -> OutIdle(state)
        | PSServer(sstate) ->
            match sstate with
            | ServerWritingCCS (si,ms,e,w,cvd,log) ->
                Pi.assume (SentCCS(Server,si.init_crand,si.init_srand,si));
                let svd = PRF.makeVerifyData si ms Server log in
                let sFinished = messageBytes HT_finished svd in
                let ki_out = ci.id_out in
                let (rg,f,_) = makeFragment ki_out CCSBytes in
                let ci = {ci with id_out = e} in
                Pi.assume(CompleteEpoch(ci.role,e));
                OutCCS(rg,f,ci,w,
                       {state with hs_outgoing = sFinished
                                   pstate = PSServer(ServerWritingFinished(si,ms,e,cvd,svd))})
            | ServerWritingCCSResume(we,w,re,r,ms,log) ->
                let si' = epochSI(we) in
                let cr' = epochCRand(we) in
                let sr' = epochSRand(we) in
                Pi.assume (SentCCS(Server,cr',sr',si'));
                let svd = PRF.makeVerifyData si' ms Server log in
                let sFinished = messageBytes HT_finished svd in
                let log = log @| sFinished in
                let ki_out = ci.id_out in
                let (rg,f,_) = makeFragment ki_out CCSBytes in
                let ci = {ci with id_out = we} in 
                OutCCS(rg,f,ci,w,
                       {state with hs_outgoing = sFinished
                                   pstate = PSServer(ClientCCSResume(re,r,svd,ms,log))})

            | _ -> OutIdle(state)
    | outBuf ->
        let ki_out = ci.id_out in
        let (rg,f,remBuf) = makeFragment ki_out outBuf in
        match remBuf with
        | xx when length xx = 0 ->
            match state.pstate with
            | PSClient(cstate) ->
                match cstate with
                | ServerCCS (_,_,_,_,_,_) ->
#if verify
                    Pi.assume(EvSentFinishedFirst(ci,true));
#endif
                    OutFinished(rg,f,{state with hs_outgoing = remBuf})
                | ClientWritingFinishedResume(cvd,svd) ->
                    let si = epochSI ki_out in
                    let op = state.poptions in
                    check_negotiation Client si op;
                    Pi.assume(EvSentFinishedFirst(ci,false));
                    OutComplete(rg,f,
                                {state with hs_outgoing = remBuf
                                            pstate = PSClient(ClientIdle(cvd,svd))})

                | _ -> OutSome(rg,f,{state with hs_outgoing = remBuf})
            | PSServer(sstate) ->
                match sstate with
                | ServerWritingFinished(si,ms,e,cvd,svd) ->
                    Pi.assume(EvSentFinishedFirst(ci,false));
                    if length si.sessionID = 0 then
                      check_negotiation Server si state.poptions;
                      OutComplete(rg,f,
                                  {state with hs_outgoing = remBuf
                                              pstate = PSServer(ServerIdle(cvd,svd))})

                    else
                      let sdb = SessionDB.insert state.sDB si.sessionID Server state.poptions.client_name (si,ms)
                      check_negotiation Server si state.poptions;
                      OutComplete(rg,f,
                                  {state with hs_outgoing = remBuf
                                              pstate = PSServer(ServerIdle(cvd,svd))   
                                              sDB = sdb})

                | ClientCCSResume(_,_,_,_,_) ->
#if verify
                    Pi.assume(EvSentFinishedFirst(ci,true));
#endif
                    OutFinished(rg,f,{state with hs_outgoing = remBuf})
                | _ -> OutSome(rg,f,{state with hs_outgoing = remBuf})
        | _ -> OutSome(rg,f,{state with hs_outgoing = remBuf})

type incoming = (* the fragment is accepted, and... *)
  | InAck of hs_state
  | InVersionAgreed of hs_state * ProtocolVersion
  | InQuery of Cert.chain * bool * hs_state
  | InFinished of hs_state
  | InComplete of hs_state
  | InError of alertDescription * string * hs_state

type incomingCCS =
  | InCCSAck of ConnectionInfo * StatefulLHAE.state * hs_state
  | InCCSError of alertDescription * string * hs_state




/// ClientKeyExchange
let find_client_cert_sign certType certAlg (distName:string list) pv hint =
    match pv with
    | TLS_1p2 ->
        let keyAlg = sigHashAlg_bySigList certAlg (cert_type_list_to_SigAlg certType) in
        Cert.for_signing certAlg hint keyAlg
    | TLS_1p1 | TLS_1p0 | SSL_3p0 ->
        let certAlg = cert_type_list_to_SigHashAlg certType pv
        let keyAlg = sigHashAlg_bySigList certAlg (cert_type_list_to_SigAlg certType) in
        Cert.for_signing certAlg hint keyAlg

let getCertificateBytes (si:SessionInfo) (cert_req:(Cert.chain * Sig.alg * Sig.skey) option) = 
  let clientCertBytes = clientCertificateBytes cert_req in
  match cert_req with
    | None when si.client_auth = true -> clientCertBytes,[]
    | Some x when si.client_auth = true -> 
        let (certList,_,_) = x in clientCertBytes,certList
    | _ when si.client_auth = false -> empty_bytes,[]

let getCertificateVerifyBytes (si:SessionInfo) (ms:PRF.masterSecret) (cert_req:(Cert.chain * Sig.alg * Sig.skey) option) (l:log) =
  match cert_req with
    | None when si.client_auth = true ->
        (* We sent an empty Certificate message, so no certificate verify message at all *)
        empty_bytes
    | Some(x) when si.client_auth = true ->
        let (certList,algs,skey) = x in
          let (mex,_) = makeCertificateVerifyBytes si ms algs skey l in
          mex
    | _ when si.client_auth = false -> 
        (* No client certificate ==> no certificateVerify message *)
        empty_bytes

#if TLSExt_sessionHash
let installSessionHash si log =
    let pv = si.protocol_version in
    let cs = si.cipher_suite in
    let alg = sessionHashAlg pv cs in
    let sh = HASH.hash alg log in
    {si with session_hash = sh}
#endif

let extract si pms (log:bytes) =
#if TLSExt_sessionHash
    let si = installSessionHash si log in
    if hasExtendedMS si.extensions then
        si, KEF.extract_extended si pms
    else
#endif
        si, KEF.extract si pms

let clientKEXBytes_RSA si config =
    if List.listLength si.serverID = 0 then
        unexpected "[clientKEXBytes_RSA] Server certificate should always be present with a RSA signing cipher suite."
    else
        match Cert.get_chain_public_encryption_key si.serverID with
        | Error(z) -> Error(z)
        | Correct(pubKey) ->
    (*KB: RSA-PMS-KEM (client) *)
            let pms = PMS.genRSA pubKey config.maxVer in
            let encpms = RSA.encrypt pubKey config.maxVer pms in
            let mex = clientKeyExchangeBytes_RSA si encpms in                                        
            correct(mex,pms)

let parseClientKEX_RSA si skey cv config data =
    if List.listLength si.serverID = 0 then
        unexpected "[parseClientKEX_RSA] when the ciphersuite can encrypt the PMS, the server certificate should always be set"
    else
        let (Correct(pk)) = Cert.get_chain_public_encryption_key si.serverID in  
        match parseClientKeyExchange_RSA si data with
        | Correct(encPMS) ->
    (*KB: RSA-PMS-KEM (server) *)
            let res = RSA.decrypt skey si cv config.check_client_version_in_pms_for_old_tls encPMS in
            correct((*RSAPMS(pk,cv,encPMS),*)res)
        | Error(z) -> Error(z)

let prepare_client_output_full_RSA (ci:ConnectionInfo) (state:hs_state) (si:SessionInfo) (cert_req:Cert.sign_cert) (log:log) : (hs_state * SessionInfo * PRF.masterSecret * log) Result =
    let clientCertBytes,certList = getCertificateBytes si cert_req in
    let si = {si with clientID = certList}
    let log = log @| clientCertBytes in

    match clientKEXBytes_RSA si state.poptions with
    | Error(z) -> Error(z)
    | Correct(v) ->
    let (clientKEXBytes,rsapms)  = v in

    //CF re-computing pk, as in clientKEXBytes; not great.
    //CF we should problably inline or redefine clientKEXbytes
    let pk = 
        match Cert.get_chain_public_encryption_key si.serverID with 
        | Correct(pk) -> pk
        | _           -> unexpected "server must have an ID"    
    let spop = state.poptions in         
    let cv = spop.maxVer in 

    (*KB: RSA-MS-KEM (client) *)
    let pms = PMS.RSAPMS(pk,cv,rsapms)
    let pmsid = pmsId pms
    let si = {si with pmsId = pmsid} in
    let log = log @| clientKEXBytes in
    let (si,ms) = extract si pms log in

    (* KB: need to prove Sig.Msg(a,PK(sk),log) here if cert_req = Some ((cl,a,sk)) *)
    Pi.assume (LMsg(cert_req,log));
    (* FIXME: here we should shred pms *)
    let certificateVerifyBytes = getCertificateVerifyBytes si ms cert_req log in

    let log = log @| certificateVerifyBytes in

    (* Enqueue current messages in output buffer *)
    let to_send = clientCertBytes @| clientKEXBytes @| certificateVerifyBytes in
    let new_outgoing = state.hs_outgoing @| to_send in
    correct ({state with hs_outgoing = new_outgoing},si,ms,log)

let sessionInfoCertBytesAuth (si:SessionInfo) (cert_req:Cert.sign_cert)= 
  if si.client_auth then
    let cb = clientCertificateBytes cert_req in
    match cert_req with
     | None -> (si,cb)
     | Some(x) -> 
         let (certList,_,_) = x in 
         ({si with clientID = certList},cb)
  else (si,empty_bytes)

let certificateVerifyBytesAuth (si:SessionInfo) (ms:PRF.masterSecret) (cert_req:Cert.sign_cert) (log:bytes) = 
        if si.client_auth then
            match cert_req with
            | None ->
                (* We sent an empty Certificate message, so no certificate verify message at all *)
                empty_bytes
            | Some(x) -> 
                let (certList,algs,skey) = x in
                let (mex,_) = makeCertificateVerifyBytes si ms algs skey log in
                mex
        else
            (* No client certificate ==> no certificateVerify message *)
            empty_bytes

let prepare_client_output_full_DHE (ci:ConnectionInfo) (state:hs_state) (si:SessionInfo) 
  (cert_req:Cert.sign_cert) (p:DHGroup.p) (g:DHGroup.g) (sy:DHGroup.elt) (log:log) : (hs_state * SessionInfo * PRF.masterSecret * log) Result =
    (* pre: Honest(verifyKey(si.server_id)) /\ StrongHS(si) -> DHGroup.PP((p,g)) /\ ServerDHE((p,g),sy,si.init_crand @| si.init_srand) *)
    (* moreover, by definition ServerDHE((p,g),sy,si.init_crand @| si.init_srand) implies ?sx.DHE.Exp((p,g),sx,sy) *)
    (*FIXME formally, the need for signing nonces is unclear *)
    
    let (si,clientCertBytes) = sessionInfoCertBytesAuth si cert_req in

    let log = log @| clientCertBytes

    (* this implements ms-KEM(DH).enc with pk = (p,g,sy) and (pv,h,l) from si *) 
    (*KB DH-PMS-KEM (client) *)
    let (cy,y,dhpms) = DH.clientGenExp p g sy in
    (* post: DHE.Exp((p,g),x,cy) *) 

    let clientKEXBytes = clientKEXExplicitBytes_DH cy in
    let log = log @| clientKEXBytes in

    (*KB DH-MS-KEM *)
    let pms = PMS.DHPMS(p,g,cy,sy,dhpms) in
    let si = {si with pmsId = pmsId pms} in
    let (si,ms) = extract si pms log in



    (* the post of this call is !sx,cy. PP((p,g) /\ DHE.Exp((p,g),cx,cy)) /\ DHE.Exp((p,g),sx,sy) -> DH.HonestExponential((p,g),cy,sy) *)
    (* thus we have Honest(verifyKey(si.server_id)) /\ StrongHS(si) -> DH.HonestExponential((p,g),cy,sy) *) 
    (* the post of this call is !p,g,gx,gy. StrongHS(si) /\ DH.HonestExponential((p,g),gx,gy) -> PRFs.Secret(ms) *)  
    (* thus we have Honest(verifyKey(si.server_id)) /\ StrongHS(si) -> PRFs.Secret(ms) *)
    (* si is now constant *)

    (*FIXME unclear what si guarantees for the ms; treated as an abstract index for now *)

    (*FIXME DHE.zeroPMS si pms; *) 

    (* KB: need to prove Sig.Msg(a,PK(sk),log) here if cert_req = Some ((cl,a,sk)) *)
    Pi.assume (LMsg(cert_req,log));

    let certificateVerifyBytes = certificateVerifyBytesAuth si ms cert_req log in

    let log = log @| certificateVerifyBytes in

    let to_send = clientCertBytes @| clientKEXBytes @| certificateVerifyBytes in
    let new_outgoing = state.hs_outgoing @| to_send in
    correct ({state with hs_outgoing = new_outgoing},si,ms,log)
(* #endif *)

 
let on_serverHello_full (ci:ConnectionInfo) crand log to_log (shello:ProtocolVersion * srand * sessionID * cipherSuite * Compression * bytes) extL =
    let log = log @| to_log in
    let (sh_server_version,sh_random,sh_session_id,sh_cipher_suite,sh_compression_method,sh_neg_extensions) = shello
    let si = { clientID = []
               client_auth = false
               serverID = []
               sessionID = sh_session_id
               protocol_version = sh_server_version
               cipher_suite = sh_cipher_suite
               compression = sh_compression_method
               extensions = extL
               init_crand = crand
               init_srand = sh_random
               session_hash = empty_bytes
               pmsId = noPmsId
               } in
    (* If DH_ANON, go into the ServerKeyExchange state, else go to the Certificate state *)
    if isAnonCipherSuite sh_cipher_suite then
        PSClient(ServerKeyExchangeDH_anon(si,log))
    elif isDHCipherSuite sh_cipher_suite then
        PSClient(ServerCertificateDH(si,log))
    elif isDHECipherSuite sh_cipher_suite then
        PSClient(ServerCertificateDHE(si,log))
    elif isRSACipherSuite sh_cipher_suite then
        PSClient(ServerCertificateRSA(si,log))
    else
        unexpected "[on_serverHello_full] Unknown ciphersuite"


let parseMessageState (ci:ConnectionInfo) state = 
    match parseMessage state.hs_incoming with
    | Error(z) -> Error(z)
    | Correct(res) ->
        match res with
        | None -> correct(None)
        | Some(x) ->
             let (rem,hstype,payload,to_log) = x in
             let state = { state with hs_incoming = rem } in
             let nx = (state,hstype,payload,to_log) in
             let res = Some(nx) in
             correct(res)

let rec recv_fragment_client (ci:ConnectionInfo) (state:hs_state) (agreedVersion:ProtocolVersion option) =
    match parseMessageState ci state with
    | Error z -> let (x,y) = z in InError(x,y,state)
    | Correct(res) ->
      match res with
      | None ->
          match agreedVersion with
          | None      -> InAck(state)
          | Some (pv) -> InVersionAgreed(state,pv)
      | Some (res) ->
      let (state,hstype,payload,to_log) = res in
      match state.pstate with
      | PSClient(cState) ->
        match hstype with
        | HT_hello_request ->
            match cState with
            | ClientIdle(_,_) -> 
                (* This is a legitimate hello request.
                   Handle it, but according to the spec do not log this message *)
                match state.poptions.honourHelloReq with
                | HRPIgnore -> recv_fragment_client ci state agreedVersion
                | HRPResume -> let (_,state) = rekey ci state state.poptions in InAck(state)       (* Terminating case, we're not idle anymore *)
                | HRPFull   -> let (_,state) = rehandshake ci state state.poptions in InAck(state) (* Terminating case, we're not idle anymore *)
            | _ -> 
                (* RFC 7.4.1.1: ignore this message *)
                recv_fragment_client ci state agreedVersion

        | HT_server_hello ->
            match cState with
            | ServerHello (crand,sid,cExtL,cvd,svd,log) ->
                match parseServerHello payload with
                | Error z -> let (x,y) = z in InError(x,y,state)
                | Correct (shello) ->
                  let (sh_server_version,sh_random,sh_session_id,sh_cipher_suite,sh_compression_method,sh_neg_extensions) = shello
                  let pop = state.poptions
                  // Sanity checks on the received message; they are security relevant. 
                  // Check that the server agreed version is between maxVer and minVer.
                  if  (geqPV sh_server_version pop.minVer 
                       && geqPV pop.maxVer sh_server_version) = false
                  then 
                    InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Protocol version negotiation",state)
                  else
                  // Check that the negotiated ciphersuite is in the proposed list.
                  // Note: if resuming a session, we still have to check that this ciphersuite is the expected one!
                  if  (List.memr state.poptions.ciphersuites sh_cipher_suite) = false
                  then 
                    InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Ciphersuite negotiation",state)
                  else
                  // Check that the compression method is in the proposed list.
                  if (List.memr state.poptions.compressions sh_compression_method) = false
                  then 
                    InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Compression method negotiation",state)
                  else
                  // Parse extensions
                  match parseServerExtensions sh_neg_extensions with
                  | Error z -> 
                      let (x,y) = z in
                      InError(x,y,state)
                  | Correct(sExtL) ->
                  // Handling of safe renegotiation //#begin-safe_renego
                  if checkServerRenegotiationInfoExtension state.poptions sExtL cvd svd then
                    //#end-safe_renego
                    // Log the received message.
                    (* Check whether we asked for resumption *)
                    if (length sid > 0) && (equalBytes sid sh_session_id) then
                        (* use resumption *)
                        (* Search for the session in our DB *)
                        match SessionDB.select state.sDB sid Client state.poptions.server_name with
                        | None ->
                            (* This can happen, although we checked for the session before starting the HS.
                                For example, the session may have expired between us sending client hello, and now. *)
                            InError(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "A session expried while it was being resumed",state)
                        | Some(storable) ->
                        let (si,ms) = storable in
                        let log = log @| to_log in
                        (* Check that protocol version, ciphersuite and compression method are indeed the correct ones *)
                        (* AP if si.sessionID = sh_session_id then This should come as a post-condition from SessionDB.select *)
                          if si.protocol_version = sh_server_version then
                            if si.cipher_suite = sh_cipher_suite then
                                if si.compression = sh_compression_method then
                                    (* Also check for resumption extension information *)
                                    if checkServerResumptionInfoExtension pop sExtL si.session_hash then
                                        let next_ci = getNextEpochs ci si crand sh_random in
                                        let nki_in = id next_ci.id_in in
                                        let nki_out = id next_ci.id_out in
                                        let (reader,writer) = PRF.keyGenClient nki_in nki_out ms in
                                        let nout = next_ci.id_out in
                                        let nin = next_ci.id_in in
                                        (* KB: the following should come from the sessiondb *)
                                        Pi.assume (Authorize(Client,si));
                                        recv_fragment_client ci 
                                            {state with pstate = PSClient(ServerCCSResume(nout,writer,
                                                                                        nin,reader,
                                                                                        ms,log))} 
                                            (Some(sh_server_version))
                                    else
                                        InError(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Wrong resumption extension information provided",state)
                                else 
                                    InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Compression method negotiation",state)
                            else 

                                InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Ciphersuite negotiation",state)
                          else 
                              InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Protocol version negotiation",state)
                        (*AP else 
                            InError(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "Session ID from DB mismatch",state) *)
                    else
                        (* we did not request resumption, or the server didn't accept it: do a full handshake *)
                        (* define the sinfo we're going to establish *)
                        match negotiateClientExtensions cExtL sExtL false (* Not resuming *) sh_cipher_suite with
                        | Error(z) -> let (x,y) = z in InError(x,y,state)
                        | Correct(nExtL) ->
                            let next_pstate = on_serverHello_full ci crand log to_log shello nExtL in
                            recv_fragment_client ci {state with pstate = next_pstate}  (Some sh_server_version)
                  else
                    InError (AD_handshake_failure,perror __SOURCE_FILE__ __LINE__ "Wrong renegotiation information provided",state)
                    
            | _ -> 
                InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "ServerHello arrived in the wrong state",state)
        | HT_certificate ->
            match cState with
            // Most of the code in the branches is duplicated, but it helps for verification
            | ServerCertificateRSA (si,log) ->
                match parseClientOrServerCertificate payload with
                | Error z ->
                    let (x,y) = z in
                    InError(x,y,state)
                | Correct(certs) ->
                    let allowedAlgs = default_sigHashAlg si.protocol_version si.cipher_suite in // In TLS 1.2, this is the same as we sent in our extension
                    if Cert.is_chain_for_key_encryption certs then
                        let advice = Cert.validate_cert_chain allowedAlgs certs in
                        match Cert.get_hint certs with
                        | None -> 
                          InQuery(certs,false,{state with pstate = PSClient(ClientCheckingCertificateRSA(si,log,certs,agreedVersion,to_log))})
                        | Some(name) -> 
                          if (name = state.poptions.server_name) then
                              InQuery(certs,advice,{state with pstate = PSClient(ClientCheckingCertificateRSA(si,log,certs,agreedVersion,to_log))})
                          else 
                              InQuery(certs,false,{state with pstate = PSClient(ClientCheckingCertificateRSA(si,log,certs,agreedVersion,to_log))})       
                    else
                        InError(AD_bad_certificate_fatal, perror __SOURCE_FILE__ __LINE__ "Server sent wrong certificate type",state)
            | ServerCertificateDHE (si,log) ->
                match parseClientOrServerCertificate payload with
                | Error z ->
                    let (x,y) = z in
                    InError(x,y,state)
                | Correct(certs) ->
                    let allowedAlgs = default_sigHashAlg si.protocol_version si.cipher_suite in // In TLS 1.2, this is the same as we sent in our extension
                    if Cert.is_chain_for_signing certs then
                        let advice = Cert.validate_cert_chain allowedAlgs certs in
                        match Cert.get_hint certs with
                        | None -> InQuery(certs,false,{state with pstate = PSClient(ClientCheckingCertificateDHE(si,log,agreedVersion,to_log))})
                        | Some(name) -> let advice = advice && (name = state.poptions.server_name) in
                                        InQuery(certs,advice,{state with pstate = PSClient(ClientCheckingCertificateDHE(si,log,agreedVersion,to_log))})
                    else
                        InError(AD_bad_certificate_fatal, perror __SOURCE_FILE__ __LINE__ "Server sent wrong certificate type",state)
            | ServerCertificateDH (si,log) -> 
                InError(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "Unimplemented",state) // TODO
            | _ -> 
                InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Certificate arrived in the wrong state",state)

        | HT_server_key_exchange ->
            match cState with
            | ServerKeyExchangeDHE(si,log) ->
                match parseServerKeyExchange_DHE si.protocol_version si.cipher_suite payload with
                | Error z ->
                    let (x,y) = z in
                    InError(x,y,state)
                | Correct(v) ->
                    let (p,g,y,alg,signature) = v in
                    match Cert.get_chain_public_signing_key si.serverID alg with
                    | Error z ->
                        let (x,y) = z in
                        InError(x,y,state)
                    | Correct(vkey) ->
                    let dheb = dheParamBytes p g y in
                    let expected = si.init_crand @| si.init_srand @| dheb in
                    if Sig.verify alg vkey expected signature then
                        let log = log @| to_log in
                        recv_fragment_client ci 
                          {state with pstate = PSClient(CertificateRequestDHE(si,p,g,y,log))} 
                          agreedVersion
                    else
                        InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "",state)
                    
            | ServerKeyExchangeDH_anon(si,log) ->
                match parseServerKeyExchange_DH_anon payload with
                | Error z -> let (x,y) = z in InError(x,y,state)
                | Correct(v) ->
                    let (p,g,y) = v in
                    let log = log @| to_log in
                    recv_fragment_client ci 
                      {state with pstate = PSClient(ServerHelloDoneDH_anon(si,p,g,y,log))} 
                      agreedVersion
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "ServerKeyExchange arrived in the wrong state",state)

        | HT_certificate_request ->
            match cState with
            | CertificateRequestRSA(si,log) ->
                (* Log the received packet *)
                let log = log @| to_log in

                (* Note: in next statement, use si, because the handshake runs according to the session we want to
                   establish, not the current one *)
                match parseCertificateRequest si.protocol_version payload with
                | Error z -> let (x,y) = z in  InError(x,y,state)
                | Correct(v) ->
                let (certType,alg,distNames) = v in
                let client_cert = find_client_cert_sign certType alg distNames si.protocol_version state.poptions.client_name in
                let si' = {si with client_auth = true} in
                (* KB: Inserted another authorize here. We lack a mechanism for the client to refuse client auth! *)
                (* AP: Client identity is fixed at protocol start. An empty identity will generate an empty certificate
                   message, which means refusal of client auth. *)
                Pi.assume (Authorize(Client,si));
#if verify
                Pi.assume (UpdatesClientAuth(si,si'));
#endif
                recv_fragment_client ci 
                  {state with pstate = PSClient(ServerHelloDoneRSA(si',client_cert,log))} 
                  agreedVersion
            | CertificateRequestDHE(si,p,g,y,log) ->
                // Duplicated code
                (* Log the received packet *)
                let log = log @| to_log in

                (* Note: in next statement, use si, because the handshake runs according to the session we want to
                   establish, not the current one *)
                match parseCertificateRequest si.protocol_version payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(v) ->
                let (certType,alg,distNames) = v in
                let client_cert = find_client_cert_sign certType alg distNames si.protocol_version state.poptions.client_name in
                let si' = {si with client_auth = true} in
                (* KB: Inserted another authorize here. We lack a mechanism for the client to refuse client auth! *)
                Pi.assume (Authorize(Client,si));
#if verify
                Pi.assume (UpdatesClientAuth(si,si'));
#endif
                recv_fragment_client ci 
                  {state with pstate = PSClient(ServerHelloDoneDHE(si',client_cert,p,g,y,log))} 
                  agreedVersion
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "CertificateRequest arrived in the wrong state",state)

        | HT_server_hello_done ->
            match cState with
            | CertificateRequestRSA(si,log) ->
                let pl = length payload in
                if pl = 0 then     
                    (* Log the received packet *)
                    let log = log @| to_log in
                    let si' = {si with client_auth = false} in

                    (* Should be provable *)
#if verify
                    Pi.assume (UpdatesClientAuth(si,si'));
#endif
                    let prep = prepare_client_output_full_RSA ci state si' None log in
                    match prep with
                    | Error z -> 
                        let (x,y) = z in 
                        InError (x,y, state)
                    | Correct z ->
                        let (state,si,ms,log) = z in
                        recv_fragment_client ci
                          {state with pstate = PSClient(ClientWritingCCS(si,ms,log))}
                          agreedVersion
                else
                    InError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "",state)
            | ServerHelloDoneRSA(si,skey,log) ->
                if length payload = 0 then
                    (* Log the received packet *)
                    let log = log @| to_log in

                    match prepare_client_output_full_RSA ci state si skey log with
                    | Error z -> 
                        let (x,y) = z in
                        InError (x,y, state)
                    | Correct z ->
                        let (state,si,ms,log) = z in
                        recv_fragment_client ci 
                          {state with pstate = PSClient(ClientWritingCCS(si,ms,log))}
                          agreedVersion
                else
                    InError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "",state)
#if verify
#else
            | ServerHelloDoneDH_anon(si,p,g,y,log) 
#endif
            | CertificateRequestDHE(si,p,g,y,log) ->
                if length payload = 0 then
                    (* Log the received packet *)
                    let log = log @| to_log in
                    let si' = {si with client_auth = false} in
                    (* Should be provable *)
#if verify
                    Pi.assume (UpdatesClientAuth(si,si'));
#endif

                    match prepare_client_output_full_DHE ci state si' None p g y log with
                    | Error z ->
                        let (x,y) = z in
                        InError (x,y, state)
                    | Correct z ->
                        let (state,si,ms,log) = z in
                        recv_fragment_client ci 
                          {state with pstate = PSClient(ClientWritingCCS(si,ms,log))}
                          agreedVersion
                else
                    InError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "",state)
            | ServerHelloDoneDHE(si,skey,p,g,y,log) ->
                if length  payload = 0 then
                    (* Log the received packet *)
                    let log = log @| to_log in

                    match prepare_client_output_full_DHE ci state si skey p g y log with
                    | Error z -> 
                        let (x,y) = z in
                        InError (x,y, state)
                    | Correct z ->
                        let (state,si,ms,log) = z in
                        recv_fragment_client ci 
                          {state with pstate = PSClient(ClientWritingCCS(si,ms,log))}
                          agreedVersion
                else
                    InError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "",state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "ServerHelloDone arrived in the wrong state",state)


        | HT_finished ->
            match cState with
            | ServerFinished(si,ms,e,cvd,log) ->
                if PRF.checkVerifyData si ms Server log payload then
                    let sDB = 
                        if length  si.sessionID = 0 then state.sDB
                        else SessionDB.insert state.sDB si.sessionID Client state.poptions.server_name (si,ms)
                    (* Should prove from checkVerifyData above *)
                    Pi.assume (CompleteEpoch(Server,ci.id_in));
                    check_negotiation Client si state.poptions;
                    InComplete({state with pstate = PSClient(ClientIdle(cvd,payload)); sDB = sDB})
                else
                    InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "Verify data did not match",state)
            | ServerFinishedResume(e,w,ms,log) ->
                let si = epochSI ci.id_in in 
                if PRF.checkVerifyData si ms Server log payload then
                    let log = log @| to_log in
                    (* Should prove from checkVerifyData above *)
                    Pi.assume (CompleteEpoch(Server,ci.id_in));
                    InFinished({state with pstate = PSClient(ClientWritingCCSResume(e,w,ms,payload,log))})
                else
                    InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "Verify data did not match",state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Finished arrived in the wrong state",state)
        | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Unrecognized message",state)
      
      (* Should never happen *)
      | PSServer(_) -> unexpected "[recv_fragment_client] should only be invoked when in client role."

let prepare_server_output_full_RSA (ci:ConnectionInfo) state si cv calgs sExtL log =
    let ext = serverExtensionsBytes sExtL in
    let serverHelloB = serverHelloBytes si si.init_srand ext in
    match Cert.for_key_encryption calgs state.poptions.server_name with
    | None -> Error(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "Could not find in the store a certificate for the negotiated ciphersuite")
    | Some(x) ->
        let (c,sk) = x in
        (* update server identity in the sinfo *)
        let si = {si with serverID = c} in
        let certificateB = serverCertificateBytes c in
        (* No ServerKeyExchange in RSA ciphersuites *)
        (* Compute the next state of the server *)
        if si.client_auth then
          let certificateRequestB = certificateRequestBytes true si.cipher_suite si.protocol_version in // true: Ask for sign-capable certificates
          let output = serverHelloB @| certificateB @| certificateRequestB @| serverHelloDoneBytes in
          (* Log the output and put it into the output buffer *)
          let log = log @| output in
          let ps = serverState ci (ClientCertificateRSA(si,cv,sk,log)) in
          correct ({state with hs_outgoing = output
                               pstate = ps},
                    si.protocol_version)
        else
          let output = serverHelloB @| certificateB @| serverHelloDoneBytes in
          (* Log the output and put it into the output buffer *)
          let log = log @| output in
          let ps = serverState ci (ClientKeyExchangeRSA(si,cv,sk,log)) in
          correct ({state with hs_outgoing = output
                               pstate = ps},
                    si.protocol_version)

let prepare_server_output_full_DH ci state si sExtL log: 'f Result =
#if avoid
  failwith "not implemented full DH"
#else
  Error(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "Unimplemented") // TODO
#endif

let prepare_server_output_full_DHE (ci:ConnectionInfo) state si certAlgs sExtL log =
    let ext = serverExtensionsBytes sExtL in
    let serverHelloB = serverHelloBytes si si.init_srand ext in
    let keyAlgs = sigHashAlg_bySigList certAlgs [sigAlg_of_ciphersuite si.cipher_suite] in
    if List.listLength keyAlgs = 0 then
        Error(AD_illegal_parameter, perror __SOURCE_FILE__ __LINE__ "The client provided inconsistent signature algorithms and ciphersuites")
    else
    match Cert.for_signing certAlgs state.poptions.server_name keyAlgs with
    | None -> Error(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "Could not find in the store a certificate for the negotiated ciphersuite")
    | Some(x) ->
        let (c,alg,sk) = x in
        (* set server identity in the session info *)
        let si = {si with serverID = c} in
        let certificateB = serverCertificateBytes c in

        (* ServerKeyExchange *)
        (*KB DH-PMS-KEM (server 1) *)
        let (p,g,y,x) = DH.serverGen() in
        //~ pms-KEM: ((p,g),y),(((p,g),y),x) = keygen_DHE() 

        let dheB = dheParamBytes p g y in
        let toSign = si.init_crand @| si.init_srand @| dheB in
        let sign = Sig.sign alg sk toSign in
        let serverKEXB = serverKeyExchangeBytes_DHE dheB alg sign si.protocol_version in

        (* CertificateRequest *)
        if si.client_auth then
          let certificateRequestB = certificateRequestBytes true si.cipher_suite si.protocol_version in // true: Ask for sign-capable certificates
          let output = serverHelloB @| certificateB @| serverKEXB @| certificateRequestB @| serverHelloDoneBytes in
          (* Log the output and put it into the output buffer *)
          let log = log @| output in
          let ps = serverState ci (ClientCertificateDHE(si,p,g,y,x,log)) in
        (* Compute the next state of the server *)
            correct (
              {state with hs_outgoing = output
                          pstate = ps},
               si.protocol_version)
        else
          let output = serverHelloB @| certificateB @| serverKEXB @| serverHelloDoneBytes in
          (* Log the output and put it into the output buffer *)
          let log = log @| output in
          let ps = serverState ci (ClientKeyExchangeDHE(si,p,g,y,x,log)) in
            correct (
              {state with hs_outgoing = output
                          pstate = ps},
               si.protocol_version)

        (* ClientKeyExchangeDHE(si,p,g,x,log) should carry PP((p,g)) /\ ?gx. DHE.Exp((p,g),x,gx) *)

let prepare_server_output_full_DH_anon (ci:ConnectionInfo) state si sExtL log : (hs_state * ProtocolVersion) Result =
    let ext = serverExtensionsBytes sExtL in
    let serverHelloB = serverHelloBytes si si.init_srand ext in
    
    (* ServerKEyExchange *)

    (*KB DH-PMS-KEM (server 1) *)
    let (p,g,y,x) = DH.serverGen() in

    let serverKEXB = serverKeyExchangeBytes_DH_anon p g y in
 
    let output = serverHelloB @|serverKEXB @| serverHelloDoneBytes in
    (* Log the output and put it into the output buffer *)
    let log = log @| output in
    (* Compute the next state of the server *)
    let ps = serverState ci (ClientKeyExchangeDH_anon(si,p,g,y,x,log)) in
    correct ({state with hs_outgoing = output
                         pstate = ps},
             si.protocol_version)

let prepare_server_output_full ci state si cv sExtL log =
    if isAnonCipherSuite si.cipher_suite then
        prepare_server_output_full_DH_anon ci state si sExtL log
    elif isDHCipherSuite si.cipher_suite then
        prepare_server_output_full_DH      ci state si sExtL log
    elif isDHECipherSuite si.cipher_suite then
        // Get the client supported SignatureAndHash algorithms. In TLS 1.2, this should be extracted from a client extension
        let calgs = default_sigHashAlg si.protocol_version si.cipher_suite in
        prepare_server_output_full_DHE ci state si calgs sExtL log
    elif isRSACipherSuite si.cipher_suite then
        // Get the client supported SignatureAndHash algorithms. In TLS 1.2, this should be extracted from a client extension
        let calgs = default_sigHashAlg si.protocol_version si.cipher_suite in
        prepare_server_output_full_RSA ci state si cv calgs sExtL log
    else
        unexpected "[prepare_server_output_full] unexpected ciphersuite"

// The server "negotiates" its first proposal included in the client's proposal
let negotiate cList sList =
    List.tryFind (fun s -> List.exists (fun c -> c = s) cList) sList

let prepare_server_output_resumption ci state crand cExtL storedSession cvd svd log =
    let (si,ms) = storedSession in
    let srand = Nonce.mkHelloRandom () in
    let (sExtL,nExtL) = negotiateServerExtensions cExtL state.poptions si.cipher_suite (cvd,svd) (Some(si.session_hash))
    let ext = serverExtensionsBytes sExtL in
    let sHelloB = serverHelloBytes si srand ext in

    let log = log @| sHelloB
    let next_ci = getNextEpochs ci si crand srand in
    let nki_in = id next_ci.id_in in
    let nki_out = id next_ci.id_out in
    let (reader,writer) = PRF.keyGenServer nki_in nki_out ms in
    {state with hs_outgoing = sHelloB
                pstate = PSServer(ServerWritingCCSResume(next_ci.id_out,writer,
                                                         next_ci.id_in,reader,
                                                         ms,log))} 

let startServerFull (ci:ConnectionInfo) state (cHello:ProtocolVersion * crand * sessionID * cipherSuites * Compression list * bytes) cExtL cvd svd log =  
    let (ch_client_version,ch_random,ch_session_id,ch_cipher_suites,ch_compression_methods,ch_extensions) = cHello in
    let cfg = state.poptions in
    // Negotiate the protocol parameters
    let version = minPV ch_client_version cfg.maxVer in
    if (geqPV version cfg.minVer) = false then
        Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Protocol version negotiation")
    else
        match negotiate ch_cipher_suites cfg.ciphersuites with
        | Some(cs) ->
            match negotiate ch_compression_methods cfg.compressions with
            | Some(cm) ->
                let sid = Nonce.random 32 in
                let srand = Nonce.mkHelloRandom () in
                let (sExtL, nExtL) = negotiateServerExtensions cExtL cfg cs (cvd, svd) None
                (* Fill in the session info we're establishing *)
                let si = { clientID         = []
                           client_auth      = cfg.request_client_certificate
                           serverID         = []
                           sessionID        = sid
                           protocol_version = version
                           cipher_suite     = cs
                           compression      = cm
                           extensions       = nExtL
                           init_crand       = ch_random
                           init_srand       = srand
                           pmsId            = noPmsId
                           session_hash     = empty_bytes}
                prepare_server_output_full ci state si ch_client_version sExtL log
            | None -> Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Compression method negotiation")
        | None ->     Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Ciphersuite negotiation")


(*CF: recursive only to enable processing of multiple messages; 
      can we loop externally, and avoid passing agreedVersion? 
      we retry iff the result is not InAck or InError. 
      What can we do after InError btw? *)

(*CF: we should rediscuss this monster pattern matching, factoring out some of it. *)

let rec recv_fragment_server (ci:ConnectionInfo) (state:hs_state) (agreedVersion:ProtocolVersion option) =
    match parseMessageState ci state with
    | Error(z) -> let (x,y) = z in  InError(x,y,state)
    | Correct(res) ->
      match res with
      | None ->
          match agreedVersion with
          | None      -> InAck(state)
          | Some (pv) -> InVersionAgreed(state,pv) (*CF: why? AP: Needed in first handshake, to check the protocol version at the record level. (See sec E.1 RFC5246) *)
      | Some (res) ->
      let (state,hstype,payload,to_log) = res in
      match state.pstate with
      | PSServer(sState) ->
        match hstype with
        | HT_client_hello ->
            match sState with
            | ClientHello(cvd,svd) | ServerIdle(cvd,svd) ->
                match parseClientHello payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct (cHello) ->
                let (ch_client_version,ch_random,ch_session_id,ch_cipher_suites,ch_compression_methods,ch_extensions) = cHello
                (* Log the received message *)
                let log = to_log in
                (* handle extensions *)
                match parseClientExtensions ch_extensions ch_cipher_suites with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(cExtL) ->
                    if checkClientRenegotiationInfoExtension state.poptions cExtL cvd then
                        if length ch_session_id = 0 then
                            (* Client asked for a full handshake *)
                            match startServerFull ci state cHello cExtL cvd svd log with 
                            | Error(z) -> let (x,y) = z in  InError(x,y,state)
                            | Correct(v) -> 
                                let (state,pv) = v in 
                                let spv = somePV pv in
                                  recv_fragment_server ci state spv
                        else
                            (* Client asked for resumption, let's see if we can satisfy the request *)
                            match SessionDB.select state.sDB ch_session_id Server state.poptions.client_name with
                            | Some sentry -> 
                                let (storedSinfo,_)  = sentry in
                                if geqPV ch_client_version storedSinfo.protocol_version
                                  && List.memr ch_cipher_suites storedSinfo.cipher_suite
                                  && List.memr ch_compression_methods storedSinfo.compression 
                                then
                                    // Check extended resumption information
#if TLSExt_sessionHash
                                    match checkClientResumptionInfoExtension state.poptions cExtL storedSinfo.session_hash with
                                    | None ->
#endif
                                        // Resumption extension not supported by client. Proceed with full handshake
                                        match startServerFull ci state cHello cExtL cvd svd log with
                                        | Correct(v) -> let (state,pv) = v in recv_fragment_server ci state (somePV (pv))
                                        | Error(z) -> let (x,y) = z in InError(x,y,state)
#if TLSExt_sessionHash
                                    | Some(true) ->
                                      (* Proceed with resumption *)
                                      let state = prepare_server_output_resumption ci state ch_random cExtL sentry cvd svd log in
                                      recv_fragment_server ci state (somePV(storedSinfo.protocol_version))
                                    | Some(false) ->
                                        InError(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Wrong safe resumption data received", state)
#endif
                                else 
                                  match startServerFull ci state cHello cExtL cvd svd log with
                                    | Correct(v) -> let (state,pv) = v in recv_fragment_server ci state (somePV (pv))
                                    | Error(z) -> let (x,y) = z in  InError(x,y,state)
                            | None ->
                                  match startServerFull ci state cHello cExtL cvd svd log with
                                    | Correct(v) -> let (state,pv) = v in recv_fragment_server ci state (somePV (pv))
                                    | Error(z) -> let (x,y) = z in  InError(x,y,state)
                    else
                        (* We don't accept an insecure client *)
                        InError(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Safe renegotiation not supported by the peer", state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "ClientHello arrived in the wrong state",state)

        | HT_certificate ->
            match sState with
            | ClientCertificateRSA (si,cv,sk,log) ->
                match parseClientOrServerCertificate payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(certs) ->
                    if Cert.is_chain_for_signing certs then
                        let advice = Cert.validate_cert_chain (default_sigHashAlg si.protocol_version si.cipher_suite) certs in
                        match Cert.get_hint certs with
                            | None -> 
                                InQuery(certs,false,
                                        {state with pstate = PSServer(ServerCheckingCertificateRSA(si,cv,sk,log,certs,to_log))})                                
                            | Some(name) when advice && (name = state.poptions.client_name) ->
                                InQuery(certs,true,
                                        {state with pstate = PSServer(ServerCheckingCertificateRSA(si,cv,sk,log,certs,to_log))})                   
                            | Some(name) ->
                                InQuery(certs,false,
                                        {state with pstate = PSServer(ServerCheckingCertificateRSA(si,cv,sk,log,certs,to_log))})
                    else
                        InError(AD_bad_certificate_fatal, perror __SOURCE_FILE__ __LINE__ "Client sent wrong certificate type",state)
            | ClientCertificateDHE (si,p,g,gx,x,log) ->
                // Duplicated code from above.
                match parseClientOrServerCertificate payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(certs) ->
                    if Cert.is_chain_for_signing certs then
                        let advice = Cert.validate_cert_chain (default_sigHashAlg si.protocol_version si.cipher_suite) certs in
                        match Cert.get_hint certs with
                            | None -> 
                                InQuery(certs,false,{state with pstate = PSServer(ServerCheckingCertificateDHE(si,p,g,gx,x,log,certs,to_log))})
                            | Some(name) when advice && (name = state.poptions.client_name) ->
                                InQuery(certs,true,{state with pstate = PSServer(ServerCheckingCertificateDHE(si,p,g,gx,x,log,certs,to_log))})
                            | Some(name) -> 
                                InQuery(certs,false,{state with pstate = PSServer(ServerCheckingCertificateDHE(si,p,g,gx,x,log,certs,to_log))})
                    else
                        InError(AD_bad_certificate_fatal, perror __SOURCE_FILE__ __LINE__ "Client sent wrong certificate type",state)
            | ClientCertificateDH  (si,log) -> (* TODO *) InError(AD_internal_error, perror __SOURCE_FILE__ __LINE__ "Unimplemented",state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Certificate arrived in the wrong state",state)

        | HT_client_key_exchange ->
            match sState with
            | ClientKeyExchangeRSA(si,cv,sk,log) ->
                match parseClientKEX_RSA si sk cv state.poptions payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(rsapms) ->
                    //CF re-computing pk, as in clientKEXBytes; not great.
                    //CF we should problably inline or redefine clientKEXbytes
                    let pk = 
                        match Cert.get_chain_public_encryption_key si.serverID with 
                        | Correct(pk) -> pk
                        | _           -> unexpected "server must have an ID"    

                (*KB: RSA-MS-KEM (server) *)
                    let pms = PMS.RSAPMS(pk,cv,rsapms)
                    let pmsid = pmsId pms
                    let si = {si with pmsId = pmsid} in
                    let log = log @| to_log in
                    let (si,ms) = extract si pms log in

                    (* TODO: we should shred the pms *)
                    (* move to new state *)
                    if si.client_auth then
#if verify
                        Pi.expect(ServerLogBeforeClientCertificateVerifyRSA(si,log));
#endif
                        recv_fragment_server ci 
                          {state with pstate = PSServer(CertificateVerify(si,ms,log))} 
                          agreedVersion
                    else
#if verify
                        Pi.expect(ServerLogBeforeClientFinishedRSA(si,log));
#endif
                        recv_fragment_server ci 
                          {state with pstate = PSServer(ClientCCS(si,ms,log))} 
                          agreedVersion
            | ClientKeyExchangeDHE(si,p,g,gx,x,log) ->
                match parseClientKEXExplicit_DH p payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(y) ->
                    let log = log @| to_log in
                    (* from the local state, we know: PP((p,g)) /\ ?gx. DHE.Exp((p,g),x,gx) ; tweak the ?gx for genPMS. *)

                    (* these 3 lines implement  ms-KEM(DH).dec where: 
                       (pv,h,l) are included in si, sk is (((p,g),gx),x), c is y *)

                    (* these 2 lines implement pms-KEM(DH).dec(pv?, sk, c) *)
            (*KB DH-PMS-KEM (server 2) *)
                    let dhpms = DH.serverExp p g gx y x in

            (*KB DH-MS-KEM *)
                    let pms = PMS.DHPMS(p,g,y,gx,dhpms) in
                    let si = {si with pmsId = pmsId(pms)} in
                    (* StrongHS(si) /\ DHE.Exp((p,g),?cx,y) -> DHE.Secret(pms) *)
                    let (si,ms) = extract si pms log in
                    (* StrongHS(si) /\ DHE.Exp((p,g),?cx,y) -> PRFs.Secret(ms) *)
                    
                    (* TODO in e.g. DHE: we should shred the pms *)
                    (* we rely on scopes & type safety to get forward secrecy*) 
                    (* move to new state *)
                    if si.client_auth then
#if verify
                        Pi.expect(ServerLogBeforeClientCertificateVerifyDHE(si,log));
                        Pi.expect(Authorize(Server,si));
                        Pi.expect(ServerLogBeforeClientCertificateVerify(si,log));
#endif
                        recv_fragment_server ci 
                          {state with pstate = PSServer(CertificateVerify(si,ms,log))} 
                          agreedVersion
                    else
                        recv_fragment_server ci 
                          {state with pstate = PSServer(ClientCCS(si,ms,log))}
                          agreedVersion
#if verify
#else
            | ClientKeyExchangeDH_anon(si,p,g,gx,x,log) ->
                match parseClientKEXExplicit_DH p payload with
                | Error(z) -> let (x,y) = z in  InError(x,y,state)
                | Correct(y) ->
                    let log = log @| to_log in
                    //MK We should also store the pmsId
            (*KB DH-PMS-KEM (server 2) *)
                    let dhpms = DH.serverExp p g gx y x in

            (*KB DH-MS-KEM *)
                    let pms = PMS.DHPMS(p,g,y,gx,dhpms) in //MK is the order of y, gx right?
                    let (si,ms) = extract si pms log in
                    (* TODO: here we should shred pms *)
                    (* move to new state *)
                    recv_fragment_server ci 
                      {state with pstate = PSServer(ClientCCS(si,ms,log))} 
                      agreedVersion
#endif
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "ClientKeyExchange arrived in the wrong state",state)

        | HT_certificate_verify ->
            match sState with
            | CertificateVerify(si,ms,log) ->
                let allowedAlgs = default_sigHashAlg si.protocol_version si.cipher_suite in // In TLS 1.2, these are the same as we sent in CertificateRequest
                let (verifyOK,_) = certificateVerifyCheck si ms allowedAlgs log payload in
                if verifyOK then// payload then
                    let log = log @| to_log in  
#if verify
                        Pi.expect(ServerLogBeforeClientFinished(si,log));
                        Pi.expect(Authorize(Server,si));
#endif
                    recv_fragment_server ci 
                      {state with pstate = PSServer(ClientCCS(si,ms,log))} 
                      agreedVersion
                else  
                    InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "Certificate verify check failed",state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "CertificateVerify arrived in the wrong state",state)

        | HT_finished ->
            match sState with
            | ClientFinished(si,ms,e,w,log) ->
                if PRF.checkVerifyData si ms Client log payload then
                    let log = log @| to_log in
                    InFinished({state with pstate = PSServer(ServerWritingCCS(si,ms,e,w,payload,log))})
                else
                    InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "Verify data did not match",state)
            | ClientFinishedResume(si,ms,e,svd,log) ->
                if PRF.checkVerifyData si ms Client log payload then
                    check_negotiation Server si state.poptions;
                    InComplete({state with pstate = PSServer(ServerIdle(payload,svd))})                       
                else
                    InError(AD_decrypt_error, perror __SOURCE_FILE__ __LINE__ "Verify data did not match",state)
            | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Finished arrived in the wrong state",state)

        | _ -> InError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "Unknown message received",state)
      (* Should never happen *)
      | PSClient(_) -> unexpected "[recv_fragment_server] should only be invoked when in server role."

let enqueue_fragment (ci:ConnectionInfo) state fragment =
    let new_inc = state.hs_incoming @| fragment in
    {state with hs_incoming = new_inc}

let recv_fragment ci (state:hs_state) (r:range) (fragment:HSFragment.fragment) =
    // FIXME: cleanup when Hs is ported to streams and deltas
    let ki_in = id ci.id_in in
    let b = HSFragment.fragmentRepr ki_in r fragment in 
    if length b = 0 then
        // Empty HS fragment are not allowed
        InError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "Empty handshake fragment received",state)
    else
        let state = enqueue_fragment ci state b in
        match state.pstate with
        | PSClient (_) -> recv_fragment_client ci state None
        | PSServer (_) -> recv_fragment_server ci state None

let recv_ccs (ci:ConnectionInfo) (state: hs_state) (r:range) (fragment:HSFragment.fragment): incomingCCS =
    // FIXME: cleanup when Hs is ported to streams and deltas
    let ki_in = id ci.id_in in
    let b = HSFragment.fragmentRepr ki_in r fragment in 
    if equalBytes b CCSBytes then  
        match state.pstate with
        | PSClient (cstate) -> // Check that we are in the right state (CCCS) 
            match cstate with
            | ServerCCS(si,ms,e,r,cvd,log) ->
                let ci = {ci with id_in = e} in
                InCCSAck(ci,r,{state with pstate = PSClient(ServerFinished(si,ms,e,cvd,log))})
            | ServerCCSResume(ew,w,er,r,ms,log) ->
                let ci = {ci with id_in = er} in
                InCCSAck(ci,r,{state with pstate = PSClient(ServerFinishedResume(ew,w,ms,log))})
            | _ -> InCCSError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "CCS arrived in the wrong state",state)
        | PSServer (sState) ->
            match sState with
            | ClientCCS(si,ms,log) ->
                let next_ci = getNextEpochs ci si si.init_crand si.init_srand in
                let nki_in = id next_ci.id_in in
                let nki_out = id next_ci.id_out in
                let (reader,writer) = PRF.keyGenServer nki_in nki_out ms in
                let ci = {ci with id_in = next_ci.id_in} in
                InCCSAck(ci,reader,{state with pstate = PSServer(ClientFinished(si,ms,next_ci.id_out,writer,log))})
            | ClientCCSResume(e,r,svd,ms,log) ->
                let ci = {ci with id_in = e} in
                InCCSAck(ci,r,{state with pstate = PSServer(ClientFinishedResume(epochSI e,ms,e,svd,log))})
            | _ -> InCCSError(AD_unexpected_message, perror __SOURCE_FILE__ __LINE__ "CCS arrived in the wrong state",state)
    else           InCCSError(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "",state)

let getMinVersion (ci:ConnectionInfo) state = 
  let pop = state.poptions in
  pop.minVer

let authorize (ci:ConnectionInfo) (state:hs_state) (q:Cert.chain) =
    let pstate = state.pstate in
    match pstate with
    | PSClient(cstate) ->
        match cstate with
        | ClientCheckingCertificateRSA(si,log,certs,agreedVersion,to_log) ->
            if certs = q then
              let log = log @| to_log in
              let si = {si with serverID = q} in
              Pi.assume (Authorize(Client,si));
              recv_fragment_client ci 
                {state with pstate = PSClient(CertificateRequestRSA(si,log))}
                agreedVersion
            else unexpected "[authorize] invoked with different cert"
        | ClientCheckingCertificateDHE(si,log,agreedVersion,to_log) ->
            let log = log @| to_log in
            let si = {si with serverID = q} in
            Pi.assume (Authorize(Client,si));
            recv_fragment_client ci 
              {state with pstate = PSClient(ServerKeyExchangeDHE(si,log))} 
              agreedVersion
        // | ClientCheckingCertificateDH -> TODO
        | _ -> unexpected "[authorize] invoked on the wrong state"
    | PSServer(sstate) ->
        match sstate with
        | ServerCheckingCertificateRSA(si,cv,sk,log,c,to_log) when c = q ->
            let log = log @| to_log in
            let si = {si with clientID = q} in
             Pi.assume (Authorize(Server,si));
            recv_fragment_server ci 
              {state with pstate = PSServer(ClientKeyExchangeRSA(si,cv,sk,log))} 
              None
        | ServerCheckingCertificateDHE(si,p,g,gx,x,log,c,to_log) when c = q ->
            let log = log @| to_log in
            let si = {si with clientID = q} in
            Pi.assume (Authorize(Server,si));
            recv_fragment_server ci 
              {state with pstate = PSServer(ClientKeyExchangeDHE(si,p,g,gx,x,log))} 
              None
        // | ServerCheckingCertificateDH -> TODO
        | _ -> unexpected "[authorize] invoked on the wrong state"

(* function used by an ideal handshake implementation to decide whether to idealize keys
let safe ki = 
    match (CS(ki), Honest(LTKey(ki, Server)), Honest(LTKey(ki,Client))) with
    | (CipherSuite (RSA, MtE (AES_256_CBC, SHA256)), true, _) -> pmsGenerated ki            
    | (CipherSuite (DHE_DSS, MtE (AES_256_CBC, SHA)), _, _) -> 
        if (TcGenerated ki) && (TsGenerated ki) then 
            true 
        else 
            false
    | _ -> false

 *)
