module RSA

// CF The check_client_... flag is included in the CRE-RSA assumption, 
// CF which seens even stronger if the adversary can choose the flag value.

open Bytes
open Error
open TLSConstants
open TLSInfo

#if ideal
// We maintain a log to look up ideal_pms values using fake_pms values.
let log = ref []
#endif

let encrypt pk pv pms =
    //#begin-ideal1
    #if ideal
    //MK here we reply on pv and pms being used only once?
    let v = if RSAKey.honest pk && CRE.honestRSAPMS pk pv pms then // MK remove CRE.honest (CRE.RSA_pms pms)??
              let fake_pms = (versionBytes pv) @|random 46
              log := (pk,pv,fake_pms,pms)::!log
              fake_pms
            else
              CRE.leakRSA pk pv pms
    //#end-ideal1
    #else
    let v = CRE.leakRSA pk pv pms
    #endif
    CoreACiphers.encrypt_pkcs1 (RSAKey.repr_of_rsapkey pk) v


//#begin-decrypt_int
let decrypt_int pk si cv cvCheck encPMS =
  (*@ Security measures described in RFC 5246, section 7.4.7.1 *)
  (*@ 1. Generate random data, 46 bytes, for PMS except client version *)
  let fakepms = random 46 in
  (*@ 2. Decrypt the message to recover plaintext *)
  let expected = versionBytes cv in
  match CoreACiphers.decrypt_pkcs1 (RSAKey.repr_of_rsaskey pk) encPMS with
    | Some pms when length pms = 48 ->
        let (clVB,postPMS) = split pms 2 in
        match si.protocol_version with
          | TLS_1p1 | TLS_1p2 ->
              (*@ 3. If new TLS version, just go on with client version and true pms.
                    This corresponds to a check of the client version number, but we'll fail later. *)
              expected @| postPMS
          
          | SSL_3p0 | TLS_1p0 ->
              (*@ 3. If check disabled, use client provided PMS, otherwise use our version number *)
              if cvCheck 
              then expected @| postPMS
              else pms
    | _  -> 
        (*@ 3. in case of decryption of length error, continue with fake PMS *) 
        expected @| fakepms
//#end-decrypt_int

let decrypt (sk:RSAKey.sk) si cv check_client_version_in_pms_for_old_tls encPMS =
    match Cert.get_chain_public_encryption_key si.serverID with
    | Error(x,y) -> unexpectedError (perror __SOURCE_FILE__ __LINE__ "The server identity should contain a valid certificate")
    | Correct(pk) ->
        let pmsb = decrypt_int sk si cv check_client_version_in_pms_for_old_tls encPMS in
        //#begin-ideal2
        #if ideal
        let Correct(pk) = (Cert.get_chain_public_encryption_key si.serverID)
        //MK Should be replaced by assoc. Is the recommended style to define it locally to facilitate refinements?
        match tryFind (fun (pk',_,fake_pms, _) -> pk'=pk && fake_pms=pmsb) !log  with
          | Some(_,_,_,ideal_pms) -> ideal_pms
          | None                -> CRE.coerceRSA pk cv pmsb
        //#end-ideal2
        #else
        CRE.coerceRSA pk cv pmsb
        #endif