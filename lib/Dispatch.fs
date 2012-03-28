﻿module Dispatch

open Bytes
open Formats
//open Record
open Tcp
open Error
open Handshake
open Alert
open TLSInfo
open SessionDB

type predispatchState =
  | Init
  | FirstHandshake
  | Finishing
  | Finished (* Only for Writing side, used to implement TLS False Start *)
  | Open
  | Closing
  | Closed

type dispatchState = predispatchState

type dState = {
    disp: dispatchState;
    conn: Record.ConnectionState;
    }

type preGlobalState = {
  poptions: protocolOptions;
  (* abstract protocol states for HS/CCS, AL, and AD *)
  handshake: Handshake.hs_state;
  alert    : Alert.state;
  appdata  : AppDataStream.app_state;

  (* connection state for reading and writing *)
  read  : dState;
  write : dState;

  (* The actual socket *)
  ns: NetworkStream;
  }

type globalState = preGlobalState

type Connection = Conn of ConnectionInfo * globalState
//type SameConnection = Connection
type nextCn = Connection
type query = Certificate.cert
// FIXME: Put the following definitions close to range and delta, and use them
type msg_i = (DataStream.range * DataStream.delta)
type msg_o = (DataStream.range * DataStream.delta)

type ioerror =
    | EInternal of ErrorCause * ErrorKind
    | EFatal of alertDescription

// Outcomes for top-level functions
type ioresult_i =
    | ReadError of ioerror
    | Close     of Tcp.NetworkStream
    | Fatal     of alertDescription
    | Warning   of nextCn * alertDescription 
    | CertQuery of nextCn * query
    | Handshaken of Connection
    | Read      of nextCn * msg_i
    | DontWrite of Connection

type ioresult_o =
    | WriteError    of ioerror
    | WriteComplete of nextCn
    | WritePartial  of nextCn * msg_o
    | MustRead      of Connection

// Outcomes for internal, one-message-at-a-time functions
type writeOutcome =
    | WriteAgain (* Possibly more data to send *)
    | WAppDataDone (* No more data to send in the current state *)
    | WHSDone
    | WMustRead (* Read until completion of Handshake *)
    | SentFatal of alertDescription
    | SentClose

type deliverOutcome =
    | RAgain
    | RAppDataDone
    | RQuery of query
    | RHSDone
    | RClose
    | RFatal of alertDescription
    | RWarning of alertDescription


let init ns role poptions =
    let outDir =
        match role with
        | Client -> CtoS
        | Server -> StoC
    let outKI = null_KeyInfo outDir poptions.minVer in
    let inKI = dual_KeyInfo outKI in
    let index = {id_in = inKI; id_out = outKI} in
    let hs = Handshake.init_handshake index role poptions in // Equivalently, inKI.sinfo
    let (send,recv) = (Record.nullConnState outKI, Record.nullConnState inKI) in
    let read_state = {disp = Init; conn = recv} in
    let write_state = {disp = Init; conn = send} in
    let al = Alert.init index in
    let app = AppDataStream.init index in
    Conn ( index,
      { poptions = poptions;
        handshake = hs;
        alert = al;
        appdata = app;
        read = read_state;
        write = write_state;
        ns=ns;})

let resume ns sid ops =
    (* Only client side, can never be server side *)

    (* Ensure the sid is in the SessionDB, and it is for a client *)
    match select ops sid with
    | None -> Error(Dispatcher,CheckFailed)
    | Some (retrieved) ->
    let (retrievedSinfo,retrievedMS,retrievedRole) = retrieved in
    match retrievedRole with
    | Server -> Error(Dispatcher,CheckFailed)
    | Client ->
    let outKI = null_KeyInfo CtoS ops.minVer in
    let inKI = dual_KeyInfo outKI in
    let index = {id_in = inKI; id_out = outKI} in
    let hs = Handshake.resume_handshake index retrievedSinfo retrievedMS ops in // equivalently, inKI.sinfo
    let (send,recv) = (Record.nullConnState outKI, Record.nullConnState inKI) in
    let read_state = {disp = Init; conn = recv} in
    let write_state = {disp = Init; conn = send} in
    let al = Alert.init index in
    let app = AppDataStream.init index in
    let res = Conn ( index,
                     { poptions = ops;
                       handshake = hs;
                       alert = al;
                       appdata = app;
                       read = read_state;
                       write = write_state;
                       ns = ns;}) in
    correct (res)

let rehandshake (Conn(id,conn)) ops =
    let new_hs = Handshake.start_rehandshake id conn.handshake ops in // Equivalently, id.id_in.sinfo
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

let rekey (Conn(id,conn)) ops =
    let new_hs = Handshake.start_rekey id conn.handshake ops in // Equivalently, id.id_in.sinfo
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

let request (Conn(id,conn)) ops =
    let new_hs = Handshake.start_hs_request id conn.handshake ops in // Equivalently, id.id_in.sinfo
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

let shutdown (Conn(id,conn)) =
    let new_al = Alert.send_alert id conn.alert AD_close_notify in
    let conn = {conn with alert = new_al} in
    Conn(id,conn)

(*
let appDataAvailable conn =
    AppDataStream.retrieve_data_available conn.appdata
*)

let getSessionInfo (Conn(id,conn)) =
    id.id_out.sinfo // in Open and Closed state, this should be equivalent to id.id_in.sinfo

let moveToOpenState (Conn(id,c)) new_storable_info =
    (* If appropriate, store this session in the DB *)
    let (storableSinfo,storableMS,storableDir) = new_storable_info in
    match storableSinfo.sessionID with
    | None -> (* This session should not be stored *) ()
    | Some (sid) -> (* SessionDB. *) insert c.poptions sid new_storable_info

    // Agreement should be on all protocols.
    // - As a pre-condition to invoke this function, we have agreement on HS protocol
    // - We have implicit agreement on appData, because the input/output buffer is empty
    //   (This can either be a pre-condition, or we can add a dynamic check here)
    // - We need to enforce agreement on the alert protocol.
    //   We do it here, by checking that our input buffer is empty. Maybe, we should have done
    //   it before, when we sent/received the CCS
    if Alert.incomingEmpty c.alert then
        let read = c.read in
        match read.disp with
        | Finishing | Finished ->
            let new_read = {read with disp = Open} in
            let c_write = c.write in
            match c_write.disp with
            | Finishing | Finished ->
                let new_write = {c_write with disp = Open} in
                let c = {c with read = new_read; write = new_write} in
                correct c
            | _ -> Error(Dispatcher,CheckFailed)
        | _ -> Error(Dispatcher,CheckFailed)
    else
        Error(Dispatcher,CheckFailed)

let closeConnection (Conn(id,c)) =
    let new_read = {c.read with disp = Closed} in
    let new_write = {c.write with disp = Closed} in
    let c = {c with read = new_read; write = new_write} in
    Conn(id,c)

(* Dispatch dealing with network sockets *)
let send ki ns dState rg ct frag =
    let (conn,data) = Record.recordPacketOut ki dState.conn rg ct frag in
    let dState = {dState with conn = conn} in
    match Tcp.write ns data with
    | Error(x,y) -> Error(x,y)
    | Correct(_) -> 
        // printf "%s(%d) " (CTtoString ct) tlen // DEBUG
        correct(dState)

(* which fragment should we send next? *)
(* we must send this fragment before restoring the connection invariant *)
let writeOne (Conn(id,c)) : (writeOutcome * Connection) Result =
  let c_read = c.read in
  let c_write = c.write in
  match c_write.disp with
  | Closed -> Error (Dispatcher,InvalidState)
  | _ ->
      let state = c.alert in
      match Alert.next_fragment id state with
      | (Alert.EmptyALFrag,_) -> 
          let hs_state = c.handshake in
          match Handshake.next_fragment id hs_state with 
          | (Handshake.EmptyHSFrag, _) ->
            let app_state = c.appdata in
                match AppDataStream.readAppDataFragment id app_state with
                | None -> (correct (WAppDataDone,Conn(id,c)))
                | Some (next) ->
                          let (tlen,f,new_app_state) = next in
                          let c = {c with appdata = new_app_state} in
                          match c_write.disp with
                          | Open ->
                          (* we send some data fragment *)
                            match send id.id_out c.ns c_write (tlen) Application_data (TLSFragment.FAppData(f)) with
                            | Correct(new_write) ->
                                let c = { c with write = new_write }
                                (* Fairly, tell we're done, and we won't write more data *)
                                (correct (WAppDataDone, Conn(id,c)) )
                            | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
                          | _ ->
                            (* We have data to send, but we cannot now. It means we're finishing a handshake.
                               Force to read, so that we'll complete the handshake and we'll be able to send
                               such data. *)
                            (* NOTE: We just ate up a fragment, which was not sent. That's not a big deal,
                               because we'll return MustRead to the app, which indeed means that no data
                               have been sent (It doesn't really matter at this point how we internally messed up
                               with the buffer, as long as we did not send anything on the network. *)
                            (correct(WMustRead, Conn(id,c)))   
          | (Handshake.CCSFrag(frag,newKeys),new_hs_state) ->
                    let (rg,ccs) = frag in
                    let (newKiOUT,newCS) = newKeys in
                    (* we send a (complete) CCS fragment *)
                    match c_write.disp with
                    | x when x = FirstHandshake || x = Open ->
                        match send id.id_out c.ns c_write rg Change_cipher_spec (TLSFragment.FCCS(ccs)) with
                        | Correct _ -> (* We don't care about next write state, because we're going to reset everything after CCS *)
                            let c = {c with handshake = new_hs_state} in
                            (* Now:
                                - update the index and install the new keys
                                - move the outgoing state to Finishing, to signal we must not send appData now. *)
                            let newID = {id with id_out = newKiOUT } in
                            let new_write = {c.write with disp = Finishing; conn = newCS} in
                            // FIXME: Should we check/reset here the alert buffer,
                            // and/or we should do it when moving to the open state?
                            
                            // let newad = AppDataStream.reset_outgoing id c.appdata in
                            let c = { c with write = new_write} in //; appdata = newad} in
                            (correct (WriteAgain, Conn(newID,c)) )
                        | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error (x,y) (* Unrecoverable error *)
                    | _ -> let closed = closeConnection (Conn(id,c)) in Error(Dispatcher, InvalidState) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSFrag(tlen,f),new_hs_state) ->     
                      (* we send some handshake fragment *)
                      match c_write.disp with
                      | x when x = Init || x = FirstHandshake ||
                               x = Finishing || x = Open ->
                          match send id.id_out c.ns c_write ( tlen) Handshake (TLSFragment.FHandshake(f)) with 
                          | Correct(new_write) ->
                            let c = { c with handshake = new_hs_state;
                                             appdata = AppDataStream.readNonAppDataFragment id c.appdata;
                                             write     = new_write }
                            (correct (WriteAgain, Conn(id,c)) )
                          | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
                      | _ -> let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,InvalidState) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSWriteSideFinished(tlen,lastFrag),new_hs_state) ->
                (* check we are in finishing state *)
                match c_write.disp with
                | Finishing ->
                    (* Send the last fragment *)
                    match send id.id_out c.ns c_write (tlen) Handshake (TLSFragment.FHandshake(lastFrag)) with 
                          | Correct(new_write) ->
                            (* Also move to the Finished state *)
                            let c_write = {new_write with disp = Finished} in
                            let c = { c with handshake = new_hs_state;
                                             appdata = AppDataStream.readNonAppDataFragment id c.appdata;
                                             write     = c_write }
                            (correct (WMustRead, Conn(id,c)))
                          | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
                | _ -> let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,InvalidState) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSFullyFinished_Write((tlen,lastFrag),new_info),new_hs_state) ->
                match c_write.disp with
                | Finishing ->
                    (* Send the last fragment *)
                    match send id.id_out c.ns c_write (tlen) Handshake (TLSFragment.FHandshake(lastFrag)) with 
                    | Correct(new_write) ->
                        let c = { c with handshake = new_hs_state;
                                         appdata = AppDataStream.readNonAppDataFragment id c.appdata;
                                         write     = new_write }
                        (* Move to the new state *)
                        // Sanity check: in and out session infos should be the same
                        if id.id_in.sinfo = id.id_out.sinfo then
                            match moveToOpenState (Conn(id,c)) new_info with
                            | Correct(c) -> (correct(WHSDone,Conn(id,c)))
                            | Error(x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) // TODO: we might want to send an alert here
                        else
                            let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,CheckFailed)
                    | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
                | _ -> let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,InvalidState) (* TODO: we might want to send an "internal error" fatal alert *)
      | (Alert.ALFrag(tlen,f),new_al_state) ->        
        match send id.id_out c.ns c_write (tlen) Alert (TLSFragment.FAlert(f)) with 
        | Correct(new_write) ->
            let new_write = {new_write with disp = Closing} in
            let ad = AppDataStream.readNonAppDataFragment id c.appdata in
            let c = { c with alert   = new_al_state;
                             appdata = ad;
                             write   = new_write }
            (correct (WriteAgain, Conn(id,c )))
        | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
      | (Alert.LastALFrag(tlen,f),new_al_state) ->
        (* We're sending a fatal alert. Send it, then close both sending and receiving sides *)
        match send id.id_out c.ns c_write (tlen) Alert (TLSFragment.FAlert(f)) with 
        | Correct(new_write) ->
            let ad = AppDataStream.readNonAppDataFragment id c.appdata in
            let c = {c with alert = new_al_state;
                            appdata = ad;
                            write = new_write}
            let closed = closeConnection (Conn(id,c)) in
            // FIXME: we need to know here which alert has been sent!
            // Needs rewriting of the Alert interface
            let inventedAlert = AD_internal_error in
            correct (SentFatal(inventedAlert), closed)
        | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)
      | (Alert.LastALCloseFrag(tlen,f),new_al_state) ->
        (* We're sending a close_notify alert. Send it, then only close our sending side.
           If we already received the other close notify, then reading is already closed,
           otherwise we wait to read it, then close. But do not close here. *)
        match send id.id_out c.ns c_write (tlen) Alert (TLSFragment.FAlert(f)) with
        | Correct(new_write) ->
            let new_write = {new_write with disp = Closed} in
            let ad = AppDataStream.readNonAppDataFragment id c.appdata in
            let c = {c with alert = new_al_state;
                            appdata = ad;
                            write = new_write}
            correct (SentClose, Conn(id,c))
        | Error (x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) (* Unrecoverable error *)

(* we have received, decrypted, and verified a record (ct,f); what to do? *)
let deliver (Conn(id,c)) ct tl frag: (deliverOutcome * Connection) Result = 
  let tlen = tl in
  let c_read = c.read in
  let c_write = c.read in
  match c_read.disp with
  | Closed -> Error(Dispatcher,InvalidState)
  | _ ->
  match (ct,frag,c_read.disp) with 

  | ContentType.Handshake, TLSFragment.FHandshake(f), x when x = Init || x = FirstHandshake || x = Finishing || x = Open ->
    let c_hs = c.handshake in
    match Handshake.recv_fragment id c_hs tlen f with
    | (Correct(corr),hs) ->
        let ad = AppDataStream.writeNonAppDataFragment id c.appdata in
        match corr with
        | Handshake.HSAck ->
            let c = { c with read = c_read; appdata = ad; handshake = hs} in
            correct (RAgain, Conn(id,c))
        | Handshake.HSVersionAgreed pv ->
            match c_read.disp with
            | Init ->
                (* Then, also c_write must be in Init state. It means this is the very first, unprotected handshake,
                   and we just negotiated the version.
                   Set the negotiated version in the current sinfo (read and write side), 
                   and move to the FirstHandshake state, so that
                   protocol version will be properly checked *)

                // Check we really are on a null session
                let id_in = id.id_in in
                let id_out = id.id_out in
                let old_in_sinfo = id_in.sinfo in
                let old_out_sinfo = id_out.sinfo in
                let c_write = c.write in
                if isNullSessionInfo old_out_sinfo && isNullSessionInfo old_in_sinfo then
                    // update the index
                    let new_sinfo = {old_out_sinfo with protocol_version = pv } in // equally with id.id_in.sinfo
                    let idIN = {id_in with sinfo = new_sinfo} in
                    let idOUT = {id_out with sinfo = new_sinfo} in
                    let newID = {id_in = idIN; id_out = idOUT} in
                    // update the state
                    let new_read = {c_read with disp = FirstHandshake; conn = Record.nullConnState idIN} in
                    let new_write = {c_write with disp = FirstHandshake; conn = Record.nullConnState idOUT} in
                    let c = {c with handshake = hs;
                                    appdata = ad;
                                    read = new_read;
                                    write = new_write} in
                    correct (RAgain, Conn(newID,c) )
                else
                    let closed = closeConnection (Conn(id,c)) in
                    Error(Dispatcher,InvalidState)
            | _ -> (* It means we are doing a re-negotiation. Don't alter the current version number, because it
                     is perfectly valid. It will be updated after the next CCS, along with all other session parameters *)
                let c = { c with read = c_read; appdata = ad; handshake = hs} in
                (correct (RAgain, Conn(id, c) ))
        | Handshake.HSQuery(query) ->
            let c = {c with read = c_read; appdata = ad; handshake = hs} in
            correct(RQuery(query),Conn(id,c))
        | Handshake.HSReadSideFinished ->
        (* Ensure we are in Finishing state *)
            match x with
            | Finishing ->
                let c = {c with read = c_read; appdata = ad; handshake = hs} in
                // Indeed, we should stop reading now!
                // (Because, except for false start implementations, the other side is now
                //  waiting for us to send our finished message)
                // However, if we say RHSDone, the library will report an early completion of HS
                // (we still have to send our finished message).
                // So, here we say ReadAgain, which will anyway first flush our output buffers,
                // this sending our finished message, and thus letting us get the WHSDone event.
                // I know, it's tricky and it sounds fishy, but that's the way it is now.
                correct (RAgain,Conn(id,c))
            | _ -> let closed = closeConnection (Conn(id,{c with handshake = hs})) in Error(Dispatcher,InvalidState) // TODO: We might want to send some alert here
        | Handshake.HSFullyFinished_Read(newSI,newMS,newDIR) ->
            let newInfo = (newSI,newMS,newDIR) in
            let c = {c with read = c_read; appdata = ad; handshake = hs} in
            (* Ensure we are in Finishing state *)
            match x with
            | Finishing ->
                // Sanity check: in and out session infos should be the same
                if id.id_in.sinfo = id.id_out.sinfo then
                    match moveToOpenState (Conn(id,c)) newInfo with
                    | Correct(c) -> correct(RHSDone, Conn(id,c))
                    | Error(x,y) -> let closed = closeConnection (Conn(id,c)) in Error(x,y) // TODO: we might want to send an alert here
                else let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,CheckFailed) // TODO: we might want to send an internal_error fatal alert here.
            | _ -> let closed = closeConnection (Conn(id,c)) in Error(Dispatcher,InvalidState) // TODO: We might want to send some alert here.
    | (Error(x,y),hs) -> let c = {c with handshake = hs} in Error(x,y) (* TODO: we might need to send some alerts *)

  | Change_cipher_spec, TLSFragment.FCCS(f), x when x = FirstHandshake || x = Open -> 
    match Handshake.recv_ccs id c.handshake tlen f with 
    | (Correct(ccs),hs) ->
        let (newKiIN,newCS) = ccs in
        let c = {c with handshake = hs} in
        let newID = {id with id_in = newKiIN} in
        let new_read = {c.read with disp = Finishing; conn = newCS} in
        // FIXME: Should we check/reset here the alert buffer,
        // and/or we should do it when moving to the open state?

        // let newad = AppDataStream.reset_incoming newID c.appdata in
        let c = { c with read = new_read} //; appdata = newad}
        correct (RAgain, Conn(newID,c))
    | (Error (x,y),hs) ->
        let c = {c with handshake = hs} in
        let closed = closeConnection (Conn(id,c)) in
        Error (x,y) // TODO: We might want to send some alert here.

  | Alert, TLSFragment.FAlert(f), _ ->
    match Alert.recv_fragment id c.alert tlen f with
    | Correct (Alert.ALAck(state)) ->
      let ad = AppDataStream.writeNonAppDataFragment id c.appdata in
      let c_read = {c_read with disp = Closing} in
      let c = {c with read = c_read; appdata = ad; alert = state} in
      correct (RAgain, Conn(id,c))
    | Correct (Alert.ALClose_notify (state)) ->
        (* An outgoing close notify has already been buffered, if necessary *)
        (* Only close the reading side of the connection *)
        let ad = AppDataStream.writeNonAppDataFragment id c.appdata in
        let new_read = {c_read with disp = Closed} in
        correct (RClose, Conn(id, { c with appdata = ad; read = new_read}))
    | Correct (Alert.ALClose (state)) ->
        (* Other fatal alert, we close both sides of the connection *)
        let ad = AppDataStream.writeNonAppDataFragment id c.appdata in
        let c = {c with appdata = ad; alert = state}
        let closed = closeConnection (Conn(id,c)) in
        // FIXME: We need to get some info about the alert we just received!
        let inventedAlert = AD_internal_error in
        correct (RFatal(inventedAlert), closed )
    | Error (x,y) -> let closed = closeConnection(Conn(id,c)) in Error(x,y) // TODO: We might want to send some alert here.

  | Application_data, TLSFragment.FAppData(f), Open -> 
    let appstate = AppDataStream.writeAppDataFragment id c.appdata (tlen) f in
    let c = {c with appdata = appstate} in
    correct (RAppDataDone, Conn(id, c))
  | _, _, _ -> let closed = closeConnection(Conn(id,c)) in Error(Dispatcher,InvalidState) // TODO: We might want to send some alert here.
  
let recv (Conn(id,c)) =
    match Tcp.read c.ns 5 with // read & parse the header
    | Error (x,y)         -> Error(x,y)
    | Correct header ->
        match Record.headerLength header with
        | Error(x,y) -> Error(x,y)
        | Correct(len) ->
        match Tcp.read c.ns len with // read & process the payload
            | Error (x,y) -> Error(x,y) 
            | Correct payload ->
                // printf "%s[%d] " (Formats.CTtoString ct) len; 
                let c_read = c.read in
                let c_read_conn = c_read.conn in
                match Record.recordPacketIn id.id_in c_read_conn (header @| payload) with
                | Error(x,y) -> Error(x,y)
                | Correct(pack) -> 
                    let (c_recv,ct,pv,tl,f) = pack in
                    if c.read.disp = Init || pv = id.id_in.sinfo.protocol_version then
                        let c_read = {c_read with conn = c_recv} in
                        let c = {c with read = c_read} in
                        correct(Conn(id,c),ct,tl,f)
                    else
                        Error(RecordVersion,CheckFailed)

let readOne c =
    match recv c with
    | Error(x,y) -> Error(x,y)
    | Correct(received) -> let (c,ct,tl,f) = received in deliver c ct tl f

let rec writeAll c =
    match writeOne c with
    | Correct (WriteAgain,c) -> writeAll c
    | other -> other

let rec read c =
    let unitVal = () in
    match writeAll c with
    | Error(x,y) -> ReadError(EInternal(x,y)) // Internal error
    | Correct(WAppDataDone,c) ->
        // Nothing more to write. We can try to read now.
        // (Note: In fact, WAppDataDone here means "nothing sent",
        // because the output buffer is always empty)
        match readOne c with
        | Correct(RAgain,c) ->
            read c
        | Correct(RAppDataDone,Conn(id,conn)) ->    
            // empty the appData internal buffer, and return its content to the user
            match AppDataStream.readAppData id conn.appdata with
            | (Some(b),appState) ->
                let conn = {conn with appdata = appState} in
                Read(Conn(id,conn),b)
            | (None,_) -> unexpectedError "[read] When RAppDataDone, some data should have been read."
        | Correct(RQuery(q),c) ->
            CertQuery(c,q)
        | Correct(RHSDone,c) ->
            Handshaken(c)
        | Correct(RClose,c) ->
            let (Conn(id,conn)) = c in
            match conn.write.disp with
            | Closed ->
                // we alreadt send a close_notify, tell the user it's over
                Close conn.ns
            | _ ->
                match writeAll c with
                | Correct(SentClose,c) ->
                    // clean shoutdown
                    Close conn.ns
                | Correct(SentFatal(ad),c) ->
                    ReadError(EFatal(ad))
                | Correct(_,c) ->
                    ReadError(EInternal(Dispatcher,Internal)) // internal error
                | Error(x,y) ->
                    ReadError(EInternal(x,y)) // internal error
        | Correct(RFatal(ad),c) ->
            Fatal(ad)
        | Correct(RWarning(ad),c) ->
            Warning(c,ad)
        | Error(x,y) ->
            ReadError(EInternal(x,y)) // internal error
    | Correct(WMustRead,c) ->
        DontWrite(c)
    | Correct(WHSDone,c) ->
        Handshaken (c)
    | Correct(SentFatal(ad),c) ->
        ReadError(EFatal(ad))
    | Correct(SentClose,c) ->
        let (Conn(id,conn)) = c in
        match conn.read.disp with
        | Closed ->
            // we already received a close_notify, tell the user it's over
            Close conn.ns
        | _ ->
            // same as we got a MustRead
            DontWrite c
    | Correct(WriteAgain,c) -> unexpectedError "[read] writeAll should never return WriteAgain"

let write (Conn(id,c)) msg =
  let (r,d) = msg in
  let new_appdata = AppDataStream.writeAppData id c.appdata r d in
  let c = {c with appdata = new_appdata} in 
  match writeAll (Conn(id,c)) with
    | Correct(WAppDataDone,Conn(id,c)) ->
        let (rdOpt,new_appdata) = AppDataStream.emptyOutgoingAppData id c.appdata in
        let c = {c with appdata = new_appdata} in
        match rdOpt with
        | None -> WriteComplete (Conn(id,c))
        | Some(rd) -> WritePartial (Conn(id,c),rd)
    | Correct(WHSDone,c) ->
        // A top-level write should never lead to HS completion.
        // Currently, we report this as an internal error.
        // Being more precise about the Dispatch state machine, we should be
        // able to prove that this case should never happen, and so use the
        // unexpectedError function.
        WriteError(EInternal(Dispatcher,Internal))
    | Correct(WMustRead,c) | Correct(SentClose,c) ->
        MustRead(c)
    | Correct(SentFatal(ad),c) ->
        WriteError(EFatal(ad))
    | Correct(WriteAgain,c) ->
        unexpectedError "[write] writeAll should never return WriteAgain"
    | Error(x,y) ->
        WriteError(EInternal(x,y)) // internal

let authorize (Conn(id,c)) q =
    let hs = Handshake.authorize c.handshake q in
    let c = {c with handshake = hs} in
    Conn(id,c)

let refuse (Conn(id,c)) (q:query) =
    let al = Alert.send_alert id c.alert AD_unknown_ca in
    let c = {c with alert = al} in
    ignore (writeAll (Conn(id,c))) // we might want to tell the user something about this

(*
let rec writeAppData c = 
    let unitVal = () in
    match writeOne c with
    | (Error (x,y),c) -> (Error(x,y),c)
    | (Correct (WriteAgain),c) -> writeAppData c
    | (Correct (Done)      ,c) -> (correct(unitVal),c)
    | (Correct (MustRead)  ,c) -> read c StopAtHS

    (* If available, read next data *)

    let c_read = conn.read in
    match c_read.disp with
    | Closed -> writeOneAppFragment conn
    | _ ->
    match Tcp.dataAvailable conn.ns with
    | Error (x,y) -> (Error(x,y),conn)
    | Correct canRead ->
    if canRead then
        match recv conn.ns c_read conn.ds_info with
        | Error (x,y) -> (Error (x,y),conn) (* TODO: if TCP error, return the error; if recoverable Record error, send Alert *)
        | Correct res ->
        let (recvSt,ct,tlen,f) = res in
        let new_read = {c_read with conn = recvSt} in
        let conn = {conn with read = new_read} in (* update the connection *)
        match deliver ct tlen f conn with
        | (Error (x,y),conn) -> (Error(x,y),conn)
        | (Correct (again),conn) ->
        if again then
            (* we just read non app-data, let's read more *)
            readNextAppFragment conn
        else
            (* We either read app-data, or a complete fatal alert,
               send buffered data *)
            writeOneAppFragment conn
    else
        (* Nothing to read, possibly send buffered data *)
        writeOneAppFragment conn
    *)