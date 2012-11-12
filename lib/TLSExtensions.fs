module TLSExtensions

open Bytes
open Error
open TLSConstants

type extensionType =
    | HExt_renegotiation_info

let extensionTypeBytes hExt =
    match hExt with
    | HExt_renegotiation_info -> [|0xFFuy; 0x01uy|]

let parseExtensionType b =
    match b with
    | [|0xFFuy; 0x01uy|] -> correct(HExt_renegotiation_info)
    | _                  -> let reason = perror __SOURCE_FILE__ __LINE__ "" in Error(AD_decode_error, reason)

let isExtensionType et (ext:extensionType * bytes) =
    let et' = fst(ext) in
    et = et'

let extensionBytes extType data =
    let extTBytes = extensionTypeBytes extType in
    let payload = vlbytes 2 data in
    extTBytes @| payload

let consExt (e:extensionType * bytes) l = e :: l

let rec parseExtensionList data list =
    match length data with
    | 0 -> correct (list)
    | x when x < 4 ->
        (* This is a parsing error, or a malformed extension *)
        Error (AD_decode_error, perror __SOURCE_FILE__ __LINE__ "")
    | _ ->
        let (extTypeBytes,rem) = Bytes.split data 2 in
        match vlsplit 2 rem with
            | Error(x,y) -> Error (x,y) (* Parsing error *)
            | Correct (res) ->
                let (payload,rem) = res in
                match parseExtensionType extTypeBytes with
                | Error(x,y) ->
                    (* Unknown extension, skip it *)
                    parseExtensionList rem list
                | Correct(extType) ->
                    let thisExt = (extType,payload) in
                    let list = consExt thisExt list in
                    parseExtensionList rem list

(* Renegotiation Info extension -- RFC 5746 *)
let renegotiationInfoExtensionBytes verifyData =
    let payload = vlbytes 1 verifyData in
    extensionBytes HExt_renegotiation_info payload

let parseRenegotiationInfoExtension payload =
    if length payload > 0 then
        vlparse 1 payload
    else
        let reason = perror __SOURCE_FILE__ __LINE__ "" in
        Error(AD_decode_error,reason)

(* Top-level extension handling *)
let extensionsBytes safeRenegoEnabled verifyData =
    if safeRenegoEnabled then
        let renInfo = renegotiationInfoExtensionBytes verifyData in
        vlbytes 2 renInfo
    else
        (* We are sending no extensions at all *)
        [||]

let parseExtensions data =
    match length data with
    | 0 -> let el = [] in correct (el)
    | 1 -> Error(AD_decode_error, perror __SOURCE_FILE__ __LINE__ "")
    | _ ->
        match vlparse 2 data with
        | Error(x,y)    -> Error(x,y)
        | Correct(exts) -> 
            match parseExtensionList exts [] with
            | Error(x,y) -> Error(x,y)
            | Correct(extList) ->
                (* Check there is at most one renegotiation_info extension *)
                // FIXME: Currently only working for renegotiation extension. Check that each extension appears only once
                let ren_ext_list = Bytes.filter (isExtensionType HExt_renegotiation_info) extList in
                if listLength ren_ext_list > 1 then
                    Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Same extension received more than once")
                else
                    correct(ren_ext_list)


let check_reneg_info payload expected =
    // We also check there were no more data in this extension.
    match parseRenegotiationInfoExtension payload with
    | Error(x,y)     -> false
    | Correct (recv) -> equalBytes recv expected

let checkClientRenegotiationInfoExtension (ren_ext_list:(extensionType * bytes) list) ch_cipher_suites expected =
    let has_SCSV = contains_TLS_EMPTY_RENEGOTIATION_INFO_SCSV ch_cipher_suites in
    if equalBytes expected [||] 
    then  
        (* First handshake *)
        if listLength ren_ext_list = 0 
        then has_SCSV
            (* either client gave SCSV and no extension; this is OK for first handshake *)
            (* or the client doesn't support this extension and we fail *)
        else
            let ren_ext = listHead ren_ext_list in
            let (extType,payload) = ren_ext in
            check_reneg_info payload expected
    else
        (* Not first handshake *)
        if has_SCSV || (listLength ren_ext_list = 0) then false
        else
            let ren_ext = listHead ren_ext_list in
            let (extType,payload) = ren_ext in
            check_reneg_info payload expected

let inspect_ServerHello_extensions (extList:(extensionType * bytes) list) expected =
    // FIXME: Only works for renegotiation info at the moment
    (* We expect to find exactly one extension *)
    match listLength extList with
    | 0 -> Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Not enough extensions given")
    | x when x <> 1 -> Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Too many extensions given")
    | _ ->
        let (extType,payload) = listHead extList in
        match extType with
        | HExt_renegotiation_info ->
            (* Check its content *)
            if check_reneg_info payload expected then
                let unitVal = () in
                correct (unitVal)
            else
                (* RFC 5746, sec 3.4: send a handshake failure alert *)
                Error(AD_handshake_failure, perror __SOURCE_FILE__ __LINE__ "Wrong renegotiation information")