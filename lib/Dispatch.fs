﻿module Dispatch

open Bytes
open Formats
open Record
open Tcp
open Error
//open Handshake
open AppData
open Alert
open TLSInfo
open TLSKey
open AppCommon
open SessionDB

type predispatchState =
  | Init (* of ProtocolVersion * ProtocolVersion *) (* min and max *)
  | FirstHandshake (* of ProtocolVersion *)             (* set by the ServerHello *) 
  | Finishing
  | Finished (* Only for Writing side, used to avoid sending data on a partially completed handshake *)
  | Open
  | Closing
  | Closed

type dispatchState = predispatchState

type dState = {
    disp: dispatchState;
    conn: ConnectionState;
    }

(* In every state, except Finishing or Finished, id_in.sinfo = id_out.sinfo must hold. *)
type index =
    { id_in:  KeyInfo;
      id_out: KeyInfo}

type preConnection = {
  poptions: protocolOptions;
  (* abstract protocol states for HS/CCS, AL, and AD *)
  handshake: Handshake.hs_state
  alert    : Alert.state
  appdata  : AppData.app_state    

  (* connection state for reading and writing *)
  read  : dState;
  write : dState;

  (* The actual socket *)
  ns: NetworkStream;
  }

type Connection = Conn of (index * preConnection)

type writeOutcome =
    | Again
    | HSStop
    | ForceStop

let init ns dir poptions =
    (* Direction "dir" is always the outgoing direction.
       So, if we are a Client, it will be CtoS, if we're a Server: StoC *)
    let hs = Handshake.init_handshake dir poptions in
    let (outKI,inKI) = (null_KeyInfo dir poptions.minVer, null_KeyInfo (dualDirection dir) poptions.minVer) in
    let (outCCS,inCCS) = (nullCCSData outKI, nullCCSData inKI) in
    let (send,recv) = (Record.initConnState outKI outCCS, Record.initConnState inKI inCCS) in
    let read_state = {disp = Init; conn = recv} in
    let write_state = {disp = Init; conn = send} in
    let al = Alert.init in
    let app = AppData.init outKI.sinfo in // or equivalently inKI.sinfo
    Conn ( {id_in = inKI; id_out = outKI},
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
    | None -> unexpectedError "[resume] requested session expired or never stored in DB"
    | Some (retrievedStoredSession) ->
    match retrievedStoredSession.dir with
    | StoC -> unexpectedError "[resume] requested session is for server side"
    | CtoS ->
    let sinfo = retrievedStoredSession.sinfo in
    let hs = Handshake.resume_handshake sinfo retrievedStoredSession.ms ops in
    let (outKI,inKI) = (null_KeyInfo CtoS ops.minVer, null_KeyInfo (dualDirection CtoS) ops.minVer) in
    let (outCCS,inCCS) = (nullCCSData outKI, nullCCSData inKI) in
    let (send,recv) = (Record.initConnState outKI outCCS, Record.initConnState inKI inCCS) in
    let read_state = {disp = Init; conn = recv} in
    let write_state = {disp = Init; conn = send} in
    let al = Alert.init in
    let app = AppData.init outKI.sinfo in // or equvalently inKI.sinfo
    let res = Conn ( {id_in = inKI; id_out = outKI},
                     { poptions = ops;
                       handshake = hs;
                       alert = al;
                       appdata = app;
                       read = read_state;
                       write = write_state;
                       ns = ns;}) in
    let unitVal = () in
    (correct (unitVal), res)

let ask_rehandshake (Conn(id,conn)) ops =
    let new_hs = Handshake.start_rehandshake conn.handshake ops in
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

let ask_rekey (Conn(id,conn)) ops =
    let new_hs = Handshake.start_rekey conn.handshake ops in
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

let ask_hs_request (Conn(id,conn)) ops =
    let new_hs = Handshake.start_hs_request conn.handshake ops in
    Conn(id,{conn with handshake = new_hs;
                       poptions = ops})

(*
let appDataAvailable conn =
    AppData.retrieve_data_available conn.appdata
*)

let getSessionInfo (Conn(id,conn)) =
    id.id_out.sinfo // in Open and Closed state, this should be equivalent to id.id_in.sinfo
   
let moveToOpenState c new_storable_info =
    (* If appropriate, store this session in the DB *)
    match new_storable_info.sinfo.sessionID with
    | None -> (* This session should not be stored *) ()
    | Some (sid) -> (* SessionDB. *) insert c.poptions sid new_storable_info

    (* FIXME: maybe we should not reset any state here...
       - AppData is already ok, checked when we move from Open to Finishing/Finished
       - Handshake should know when the handshake is done, so it should be in the right state already
       - Alert... if we need to send some alert, why deleting them now?
       Commenting next lines for now.
        *)
    (*
    let new_info = new_storable_info.sinfo in
    let new_hs = Handshake.new_session_idle c.handshake new_info new_storable_info.ms in
    let new_alert = Alert.init in
    (* FIXME: here we silenty reset appdata buffers. However, if they are not empty now
       we should at least report some error to the user. *)
    let new_appdata = AppData.init new_info in
    (* Read and write state should already have the same SessionInfo
        set after CCS, check it *)
    let c = {c with handshake = new_hs;
                    alert = new_alert;
                    appdata = new_appdata} in
    *)
    let read = c.read in
    match read.disp with
    | Finishing | Finished ->
        let new_read = {read with disp = Open} in
        let c_write = c.write in
        match c_write.disp with
        | Finishing | Finished ->
            let new_write = {c_write with disp = Open} in
            {c with read = new_read; write = new_write}
        | _ -> unexpectedError "[moveToOpenState] should only work on Finishing or Finished write states"
    | _ -> unexpectedError "[moveToOpenState] should only work on Finishing read states"

let closeConnection (Conn(id,c)) =
    let new_read = {c.read with disp = Closed} in
    let new_write = {c.write with disp = Closed} in
    let c = {c with read = new_read; write = new_write} in
    Conn(id,c)

(* Dispatch dealing with network sockets *)
let send ki ns conn tlen ct frag =
    let (conn,data) = Record.recordPacketOut ki conn tlen ct frag in
    match Tcp.write ns data with
    | Error(x,y) -> Error(x,y)
    | Correct(_) -> 
        printf "%s(%d) " (CTtoString ct) tlen 
        correct(conn)

(* which fragment should we send next? *)
(* we must send this fragment before restoring the connection invariant *)
let next_fragment (Conn(id,c)) : (dispatchOutcome Result) * Connection =
  let c_write = c.write in
  match c_write.disp with
  | Closed -> unexpectedError "[next_fragment] should never be invoked on a closed connection."
  | _ ->
      let state = c.alert in
      match Alert.next_fragment id.id_out state with
      | (EmptyALFrag,_) -> 
          let hs_state = c.handshake in
          match Handshake.next_fragment hs_state with 
          | (Handshake.EmptyHSFrag, _) ->
            let app_state = c.appdata in
                match AppData.next_fragment id.id_out app_state with
                | None -> (* nothing to do (tell the caller) *)
                          (correct (ForceStop),Conn(id,c))
                | Some ((tlen,f),new_app_state) ->
                          match c_write.disp with
                          | Open ->
                          (* we send some data fragment *)
                            match send id.id_out c.ns c_write.conn tlen Application_data f with
                            | Correct(ss) ->
                                let new_write = { c_write with conn = ss } in
                                let c = { c with appdata = new_app_state;
                                                 write = new_write }
                                (* We just sent one appData fragment, we don't want to write anymore for this round *)
                                (correct (ForceStop), Conn(id,c) )
                            | Error (x,y) -> (Error(x,y), closeConnection (Conn(id,c))) (* Unrecoverable error *)
                          | _ -> (Error(Dispatcher,InvalidState), closeConnection (Conn(id,c))) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.CCSFrag((tlen,ccs),(newKiOUT,ccs_data)),new_hs_state) ->
                    (* we send a (complete) CCS fragment *)
                    match c_write.disp with
                    | x when x = FirstHandshake || x = Open ->
                        match send id.id_out c.ns c_write.conn tlen Change_cipher_spec ccs with
                        | Correct ss ->
                            (* It is safe to swtich to the new session if
                               the appData outgoing buffer is empty,
                               or the next session is compatible with the old one.
                            *)
                            (* Implementation note: order of next OR is important:
                               In the first handshake, AppData buffer will be empty, and it does not
                               make sense to invoke isCompatibleSession. *)
                            if AppData.is_outgoing_empty id.id_out.sinfo c.appdata
                               || c.poptions.isCompatibleSession id.id_out.sinfo newKiOUT.sinfo then
                                (* Now:
                                    - update the outgoing index in Dispatch
                                    - update the outgoing keys in Record
                                    - move the outgoing state to Finishing, to signal we must not send appData now. *)
                                let id = {id with id_out = newKiOUT } in
                                let ss = Record.initConnState id.id_out ccs_data in
                                let new_write = {disp = Finishing; conn = ss} in
                                let c = { c with handshake = new_hs_state;
                                                             write = new_write }
                                (correct (Again), Conn(id,c) )
                            else
                                (Error(Dispatcher, InvalidState), closeConnection (Conn(id,c))) (* TODO: we might want to send an "internal error" fatal alert *)
                        | Error (x,y) -> (Error (x,y), closeConnection (Conn(id,c))) (* Unrecoverable error *)
                    | _ -> (Error(Dispatcher, InvalidState), closeConnection (Conn(id,c))) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSFrag((tlen,f)),new_hs_state) ->     
                      (* we send some handshake fragment *)
                      match c_write.disp with
                      | x when x = Init || x = FirstHandshake ||
                               x = Finishing || x = Open ->
                          match send id.id_out c.ns c_write.conn tlen Handshake f with 
                          | Correct(ss) ->
                            let new_write = {c_write with conn = ss} in
                            let c = { c with handshake = new_hs_state;
                                             write     = new_write }
                            (correct (Again), Conn(id,c) )
                          | Error (x,y) -> (Error(x,y), closeConnection (Conn(id,c))) (* Unrecoverable error *)
                      | _ -> (Error(Dispatcher,InvalidState), closeConnection (Conn(id,c))) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSWriteSideFinished((tlen,lastFrag)),new_hs_state) ->
                (* check we are in finishing state *)
                match c_write.disp with
                | Finishing ->
                    (* Send the last fragment *)
                    match send id.id_out c.ns c_write.conn tlen Handshake lastFrag with 
                          | Correct(ss) ->
                           (* Also move to the Finished state *)
                            let c_write = {c_write with conn = ss;
                                                        disp = Finished} in
                            let c = { c with handshake = new_hs_state;
                                             write     = c_write }
                            (correct (false), Conn(id,c))
                          | Error (x,y) -> (Error(x,y), closeConnection (Conn(id,c))) (* Unrecoverable error *)
                | _ -> (Error(Dispatcher,InvalidState), closeConnection (Conn(id,c))) (* TODO: we might want to send an "internal error" fatal alert *)
          | (Handshake.HSFullyFinished_Write((tlen,lastFrag),new_info),new_hs_state) ->
                match c_write.disp with
                | Finishing ->
                   (* according to the protocol logic and the dispatcher
                      implementation, we must now have an empty input buffer.
                      This means we can directly report a NewSessionInfo error
                      notification, and not a mustRead.
                      Check thus that we in fact have an empty input buffer *)
                   if not (AppData.is_incoming_empty c.appdata) then (* this is a bug. *)
                       (Error(Dispatcher,Internal), closeConnection c) (* TODO: we might want to send an "internal error" fatal alert *)
                   else
                       (* Send the last fragment *)
                       match send c.ns c_write.conn tlen Handshake lastFrag with 
                       | Correct(ss) ->
                         let new_write = {c_write with conn = ss} in
                         let c = { c with handshake = new_hs_state;
                                          write     = new_write }
                         (* Move to the new state *)
                         let c = moveToOpenState c new_info in
                         (Error(NewSessionInfo,Notification),c)
                       | Error (x,y) -> (Error(x,y), closeConnection c) (* Unrecoverable error *)
                | _ -> (Error(Dispatcher,InvalidState), closeConnection c) (* TODO: we might want to send an "internal error" fatal alert *)
      | (ALFrag(tlen,f),new_al_state) ->        
        match send c.ns c_write.conn tlen Alert f with 
        | Correct ss ->
            let new_write = {disp = Closing; conn = ss} in
            (correct (true), { c with alert = new_al_state;
                                      write   = new_write } )
        | Error (x,y) -> (Error(x,y), closeConnection c) (* Unrecoverable error *)
      | (LastALFrag(tlen,f),new_al_state) ->
        (* Same as above, but we set Closed dispatch state, instead of Closing *)
        match send c.ns c_write.conn tlen Alert f with 
        | Correct ss ->
            let new_write = {disp = Closed; conn = ss} in
            (* FIXME: if also the reading state is closed, return an error to notify the user
               that the communication is over. Otherwise we can enter infinte loops polling for data
               that will never arrive *)
            (correct (false), { c with alert = new_al_state;
                                       write   = new_write } )
        | Error (x,y) -> (Error(x,y), closeConnection c) (* Unrecoverable error *)

let rec writeOneAppFragment c =
    (* Writes *at most* one application data fragment. This might send no appdata fragment if
       - The handshake finishes (write side or fully)
       - An alert has been sent
       - We sent one (or more) other protocol messages and now we can read some data
     *)
    let unitVal = () in
    let c_write = c.write in
    match c_write.disp with
    | Closed -> (correct(unitVal),c)
    | _ ->
        match next_fragment c with
        | (Error (x,y),c) -> (Error(x,y),c)
        | (Correct (again),c) ->
        if again then
            (* be fair: don't do more sending now if we could read *)
            (* note: eventually all buffered data will be sent, they're are already committed
                        to be sent *)
            match Tcp.dataAvailable c.ns with
            | Error (x,y) -> (correct (unitVal),c) (* There's an error with TCP, but we can ignore it right now, and just pretend there are data to send, so the error will show up next time *)
            | Correct dataAv ->
            if dataAv then
                (correct(unitVal),c)
            else
                writeOneAppFragment c 
        else
            (correct (unitVal),c)

(* we have received, decrypted, and verified a record (ct,f); what to do? *)
let deliver ct tlen f c = 
  let c_read = c.read in
  match c_read.disp with
  | Closed -> unexpectedError "[deliver] should never be invoked on a closed connection state."
  | _ ->
  match (ct,c_read.disp) with 

  | Handshake, x when x = Init || x = FirstHandshake || x = Finishing || x = Open ->
    match Handshake.recv_fragment c.handshake tlen f with
    | (Correct(corr),hs) ->
        match corr with
        | Handshake.HSAck ->
            (correct (true), { c with handshake = hs} )
        | Handshake.HSVersionAgreed pv ->
            match c_read.disp with
            | Init ->
                (* Then, also c_write must be in Init state. It means this is the very first, unprotected handshake,
                   and we just negotiated the version.
                   Set the negotiated version in the current sinfo, 
                   and move to the FirstHandshake state, so that
                   protocol version will be properly checked *)
                let new_sinfo = {c.ds_info with protocol_version = pv } in
                let new_read = {c_read with disp = FirstHandshake} in
                let new_write = {c.write with disp = FirstHandshake} in
                (correct (true), { c with   ds_info = new_sinfo;
                                            handshake = hs;
                                            read = new_read;
                                            write = new_write} )
            | _ -> (* It means we are doing a re-negotiation. Don't alter the current version number, because it
                     is perfectly valid. It will be updated after the next CCS, along with all other session parameters *)
                ((correct (true), { c with handshake = hs} ))
        | Handshake.HSReadSideFinished ->
        (* Ensure we are in Finishing state *)
            match x with
            | Finishing ->
                (* We stop reading now. The subsequent writes invoked after
                   reading will send the appropriate handshake messages, and
                   the handshake will be fully completed *)
                (correct (false),{c with handshake = hs})
            | _ -> (Error(Dispatcher,InvalidState), {c with handshake = hs} )
        | Handshake.HSFullyFinished_Read(new_info) ->
            let c = {c with handshake = hs} in
            (* Ensure we are in Finishing state *)
            match x with
            | Finishing ->
                let c = moveToOpenState c new_info in
                (Error(NewSessionInfo,Notification),c)
            | _ -> (Error(Dispatcher,InvalidState), c)
    | (Error(x,y),hs) -> (Error(x,y),{c with handshake = hs}) (* TODO: we might need to send some alerts *)

  | Change_cipher_spec, x when x = FirstHandshake || x = Open -> 
    match Handshake.recv_ccs c.handshake tlen f with 
    | (Correct(cryptoparams),hs) ->
        let new_recv = Record.recv_setCrypto cryptoparams in
        let new_read = {disp = Finishing; conn = new_recv} in
        (* Next statement should have no effect, since we should reach this
           code always with an empty input buffer *)
        let new_appdata = AppData.reset_incoming c.appdata in
        (correct (true), { c with handshake = hs;
                                  read = new_read;
                                  appdata = new_appdata} )
    | (Error (x,y),hs) -> (Error (x,y), {c with handshake = hs})

  | Alert, x ->
    match Alert.recv_fragment c.alert tlen f with
    | Correct (ALAck(state)) ->
      (correct (true), { c with alert = state})
    | Correct (ALClose_notify (state)) ->
        (* An outgoing close notify has already been buffered, if necessary *)
        (* Only close the reading side of the connection *)
        let new_read = {c_read with disp = Closed} in
        (correct (false), { c with read = new_read})
    | Correct (ALClose (state)) ->
        (* Other fatal alert, we close both sides of the connection *)
        let new_read = {c_read with disp = Closed} in
        let new_write = {c.write with disp = Closed} in
        (* FIXME: this generates an infinite loop. We should report an error to the user instead *)
        (correct (false), { c with read = new_read;
                                   write = new_write} )
    | Error (x,y) -> (Error(x,y),c) (* Always fatal, so don't need to track the current alert state? *)

  | Application_data, Open -> 
    let appstate = AppData.recv_fragment c.appdata tlen f in
    (correct (false), { c with appdata = appstate })
  | _, _ -> (Error(Dispatcher,InvalidState),c)
  
let recv ns readState sinfo =
    match Tcp.read ns 5 with // read & parse the header
    | Error (x,y)         -> Error(x,y)
    | Correct header ->
        match parseHeader header with
        | Error(x,y)      -> Error(x,y)
        // enforce the protocol version (once established)
        | Correct (ct,pv,len) when readState.disp = Init || pv = sinfo.protocol_version ->
            match Tcp.read ns len with // read & process the payload
            | Error (x,y) -> Error(x,y) 
            | Correct payload ->
                // printf "%s[%d] " (Formats.CTtoString ct) len; 
                Record.recordPacketIn readState.conn len ct payload
        | _ -> Error(RecordVersion,CheckFailed)

let rec readNextAppFragment conn =
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

let commit conn b =
    let new_appdata = AppData.send_data conn.appdata b in
    {conn with appdata = new_appdata}

let write_buffer_empty conn =
    AppData.is_outgoing_empty conn.appdata

let readOneAppFragment conn =
    (* Similar to the OpenSSL strategy *)
    let c_appdata = conn.appdata in
    if not (AppData.is_incoming_empty c_appdata) then
        (* Read from the buffer *)
        let (read, new_appdata) = AppData.retrieve_data c_appdata in
        let conn = {conn with appdata = new_appdata} in
        (correct (read),conn)
    else
        (* Read from the TCP socket *)
        match readNextAppFragment conn with
        | (Correct (x),conn) ->
            (* One fragment may have been put in the buffer *)
            let c_appdata = conn.appdata in
            let (read, new_appdata) = AppData.retrieve_data c_appdata in
            let conn = {conn with appdata = new_appdata} in
            (correct (read),conn)
        | (Error (x,y),c) -> (Error(x,y),c)