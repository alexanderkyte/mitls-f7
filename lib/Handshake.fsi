﻿(* Handshake protocol *) 
module Handshake

open Data
open Record
open Error_handling
open Formats
open HS_msg
open Sessions
open AppCommon

type protoState

type hs_state

val init_handshake: role -> protocolOptions -> SessionInfo * hs_state
(*
val rehandshake: hs_state -> hs_state Result (* new handshake on same connection *)
val rekey: hs_state -> hs_state Result (* resume on same connection *)
val resume: SessionInfo -> hs_state (* resume on different connection; only client-side *)
*)

type HSFragReply =
  | EmptyHSFrag
  | HSFrag of bytes
  | HSWriteSideFinished
  | HSFullyFinished_Write of SessionInfo
  | CCSFrag of bytes * ccs_data

val next_fragment: hs_state -> int -> (HSFragReply * hs_state)

type recv_reply = 
  | HSAck      (* fragment accepted, no visible effect so far *)
  | HSChangeVersion of role * ProtocolVersionType 
                          (* ..., and we should use this new protocol version for sending *) 
  | HSReadSideFinished
  | HSFullyFinished_Read of SessionInfo (* ..., and we can start sending data on the connection *)

(*type hs_output_reply = 
  | HS_Fragment of bytes
  | HS_CCS of ccs_data (* new ccs data *)
  | Idle*)

val recv_fragment: hs_state -> fragment -> (recv_reply Result) * hs_state
val recv_ccs: hs_state -> fragment -> (ccs_data Result) * hs_state

val updateSessionInfo: hs_state -> SessionInfo -> hs_state