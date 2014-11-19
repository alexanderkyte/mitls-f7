﻿#light "off"
/// <summary>
/// Module handling the application state.
/// </summary>
module FlexTLS.FlexState

open NLog

open Bytes
open TLSInfo

open FlexTypes



/// <summary>
/// Module handling the application state.
/// </summary>
type FlexState =
    class

    static member guessNextEpoch (e:epoch) (nsc:nextSecurityContext) : epoch =
        if nsc.si.init_crand = nsc.crand && nsc.si.init_srand = nsc.srand then
            // full handshake
            TLSInfo.fullEpoch e nsc.si
        else
            // abbreviated handshake
            // we set do dummy values most of the fields here, but at least
            // we store it's an abbreviated handshake
            let ai = {abbr_crand = nsc.crand;
                      abbr_srand = nsc.srand;
                      abbr_session_hash = nsc.si.session_hash;
                      abbr_vd = None} in
            // The last 'e' parameters is most certainly wrong, as it should be
            // the previous epoch of the handshake that generated the session.
            // We could put an empty epoch instead, but it's difficult to get from the miTLS API
            // In practice, we don't care about its value anyway.
            TLSInfo.abbrEpoch e ai nsc.si e

    /// <summary>
    /// Update the Handshake log with the bytes received or sent by the Record Level
    /// </summary>
    /// <param name="st"> State to update the hs_log with </param>
    /// <param name="log"> Log of the handshake </param>
    /// <returns> The state with the updated log </returns>
    static member updateHandshakeLog (st:state) (log:bytes) :state =
        let hs_log = st.hs_log @| log in
        {st with hs_log = hs_log}

    /// <summary>
    /// Update the log according to the given content type (currently only the Handshake log is maintained)
    /// </summary>
    /// <param name="st"> State to be updated </param>
    /// <param name="ct"> Content type </param>
    /// <param name="log"> Data to be logged </param>
    /// <returns> The state with the updated log </returns>
    static member updateLog (st:state) (ct:TLSConstants.ContentType) (log:bytes) : state =
        match ct with
        | TLSConstants.Handshake ->
            FlexState.updateHandshakeLog st log
        | TLSConstants.Application_data
        | TLSConstants.Alert
        | TLSConstants.Change_cipher_spec -> st

    static member resetHandshakeLog (st:state) : state =
        {st with hs_log = empty_bytes}

    static member resetLog (st:state) (ct:TLSConstants.ContentType) : state =
        match ct with
        | TLSConstants.Handshake ->
            FlexState.resetHandshakeLog st
        | TLSConstants.Application_data
        | TLSConstants.Alert
        | TLSConstants.Change_cipher_spec -> st

    static member resetLogs (st:state) : state =
        // Add here reset for other logs if we ever add them
        FlexState.resetLog st TLSConstants.Handshake

    /// <summary> Update the state with a new readin (incoming) record </summary>
    static member updateIncomingRecord (st:state) (incoming:Record.recvState) : state =
        let read_s = {st.read with record = incoming} in
        {st with read = read_s}

    /// <summary> Update the state with a new reading (incoming) epoch </summary>
    static member updateIncomingEpoch (st:state) (e:TLSInfo.epoch) : state =
        let read_s = {st.read with epoch = e} in
        {st with read = read_s}

    /// <summary> Update the state with new reading (incoming) keys </summary>
    /// <remarks> This field is informational only; the new keys will not be used to encrypt future messages.
    /// To change encryption keys, update the incoming record instead. </remarks>
    static member updateIncomingKeys (st:state) (keys:keys) : state =
        let read_s = {st.read with keys = keys} in
        {st with read = read_s}

    /// <summary> Update the state with verify data on the reading channel </summary>
    static member updateIncomingVerifyData (st:state) (verify_data:bytes) : state =
        let read_s = {st.read with verify_data = verify_data} in
        {st with read = read_s}

    /// <summary> Update the state with the initial epoch protocol version </summary>
    /// <remarks> The user typically doesn't need to invoke this function. It is invoked when receiving a
    /// ServerHello message, to set the protocol version for the first handshake on a connection. </remarks>
    static member updateIncomingRecordEpochInitPV (st:state) (pv:TLSConstants.ProtocolVersion) : state =
        let read_s = {st.read with epoch_init_pv = pv} in
        {st with read = read_s}

    /// <summary> Update the state with a new Handshake buffer value </summary>
    static member updateIncomingHSBuffer (st:state) (buf:bytes) : state =
        let read_s = {st.read with hs_buffer = buf} in
        {st with read = read_s}

    /// <summary> Update the state with a new Alert buffer value </summary>
    static member updateIncomingAlertBuffer (st:state) (buf:bytes) : state =
        let read_s = {st.read with alert_buffer = buf} in
        {st with read = read_s}

    /// <summary> Update the state with a new Application Data buffer value </summary>
    static member updateIncomingAppDataBuffer (st:state) (buf:bytes) : state =
        let read_s = {st.read with appdata_buffer = buf} in
        {st with read = read_s}

    /// <summary> Update the state with a new buffer value for a specific content type </summary>
    static member updateIncomingBuffer st ct buf =
        match ct with
        | TLSConstants.Alert -> FlexState.updateIncomingAlertBuffer st buf
        | TLSConstants.Handshake -> FlexState.updateIncomingHSBuffer st buf
        | TLSConstants.Application_data -> FlexState.updateIncomingAppDataBuffer st buf
        | TLSConstants.Change_cipher_spec -> st

    /// <summary>
    /// Install Reading Keys into the current state
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <returns> Updated state </returns>
    static member installReadKeys (st:state) (nsc:nextSecurityContext): state =
        LogManager.GetLogger("file").Debug("@ Install Read Keys");
        let nextEpoch = FlexState.guessNextEpoch st.read.epoch nsc in
        let rk,_ = nsc.keys.epoch_keys in
        let ark = StatefulLHAE.COERCE (id nextEpoch) TLSInfo.Reader rk in
        let nextRecord = Record.initConnState nextEpoch TLSInfo.Reader ark in
        let st = FlexState.updateIncomingRecord st nextRecord in
        let st = FlexState.updateIncomingEpoch st nextEpoch in
        let st = FlexState.updateIncomingKeys st nsc.keys in
        st

    /// <summary> Update the state with a new outgoing record </summary>
    static member updateOutgoingRecord (st:state) (outgoing:Record.sendState) : state =
        let write_s = {st.write with record = outgoing} in
        {st with write = write_s}

    /// <summary> Update the state with a new epoch </summary>
    static member updateOutgoingEpoch (st:state) (e:TLSInfo.epoch) : state =
        let write_s = {st.write with epoch = e} in
        {st with write = write_s}

    /// <summary> Update the state with new keys </summary>
    /// <remarks> This field is informational only; the new keys will not be used to encrypt future messages.
    static member updateOutgoingKeys (st:state) (keys:keys) : state =
        let write_s = {st.write with keys = keys} in
        {st with write = write_s}

    /// <summary> Update the state with verify data on the writing channel </summary>
    static member updateOutgoingVerifyData (st:state) (verify_data:bytes) : state =
        let write_s = {st.write with verify_data = verify_data} in
        {st with write = write_s}

    /// <summary> Update the state initial epoch protocol version </summary>
    static member updateOutgoingRecordEpochInitPV (st:state) (pv:TLSConstants.ProtocolVersion) : state =
        let write_s = {st.write with epoch_init_pv = pv} in
        {st with write = write_s}

    /// <summary> Update the state with a new Handshake buffer value </summary>
    static member updateOutgoingHSBuffer (st:state) (buf:bytes) : state =
        let write_s = {st.write with hs_buffer = buf} in
        {st with write = write_s}

    /// <summary> Update the state with a new Alert buffer value </summary>
    static member updateOutgoingAlertBuffer (st:state) (buf:bytes) : state =
        let write_s = {st.write with alert_buffer = buf} in
        {st with write = write_s}

    /// <summary> Update the state with a new Application Data buffer value </summary>
    static member updateOutgoingAppDataBuffer (st:state) (buf:bytes) : state =
        let write_s = {st.write with appdata_buffer = buf} in
        {st with write = write_s}

    /// <summary> Update the state with a new buffer value for a specific content type </summary>
    static member updateOutgoingBuffer st ct buf =
        match ct with
        | TLSConstants.Alert -> FlexState.updateOutgoingAlertBuffer st buf
        | TLSConstants.Handshake -> FlexState.updateOutgoingHSBuffer st buf
        | TLSConstants.Application_data -> FlexState.updateOutgoingAppDataBuffer st buf
        | TLSConstants.Change_cipher_spec -> st
    
    /// <summary>
    /// Install Writing Keys into the current state
    /// </summary>
    /// <param name="st"> State of the current Handshake </param>
    /// <param name="nsc"> Next security context being negociated </param>
    /// <returns> Updated state </returns>
    static member installWriteKeys (st:state) (nsc:nextSecurityContext) : state =
        LogManager.GetLogger("file").Debug("@ Install Write Keys");
        let nextEpoch = FlexState.guessNextEpoch st.write.epoch nsc in
        let _,wk = nsc.keys.epoch_keys in
        let awk = StatefulLHAE.COERCE (id nextEpoch) TLSInfo.Writer wk in
        let nextRecord = Record.initConnState nextEpoch TLSInfo.Writer awk in
        let st = FlexState.updateOutgoingRecord st nextRecord in
        let st = FlexState.updateOutgoingEpoch st nextEpoch in
        let st = FlexState.updateOutgoingKeys st nsc.keys in
        st
      
    end
