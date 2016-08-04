﻿module GenerativeTypeProviderExample.Regarder 

open GenerativeTypeProviderExample.CommunicationAgents
open GenerativeTypeProviderExample.IO

let mutable dico = Map.empty<string,AgentRouter>
let mutable changed = false

let ajouter str agent =
    if not(changed) then
        dico <- dico.Add(str,agent)
        changed <- true

let startAgentRouter agent =
    dico.Item(agent).Start()

let sendMessage agent message role =
    dico.Item(agent).SendMessage(message,role)


let receiveMessage agent message role listTypes =
    dico.Item(agent).ReceiveMessage(message,role,listTypes) 
    //"agent" args message role (toList event.Payload)