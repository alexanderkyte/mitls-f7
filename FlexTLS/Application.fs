#light "off"

module Application

open TLSInfo
open TLSConstants

open FlexTLS
open FlexTypes
open FlexConstants
open FlexAlert
open FlexState
open FlexClientHello
open FlexServerHello
open FlexHandshake
open FlexAttacks

open Bytes
open TLSError


let _ =
    
    (* Initiate a record to store all exchanged Handshake messages *)
    let sms = nullFHSMessages in

    (* Establish a Tcp connection to a peer by listening or sending on a socket *)
    let st,cfg = FlexTLS.openConnection (Client,"128.93.189.207","prosecco.fr",4433) in

    (* Ready for handshake using either one of the top-level APIs or the Flex|Message| methods *)
    let st = FlexAttacks.runAlertPlaintextAttack st in 

    (* Ready for application data *)
    printf "Ready for application data !\n";
    System.Console.ReadLine()
