import React, { createContext, useReducer } from 'react'
import EventEmitter from 'eventemitter3';

export const AppContext = createContext({});
export const AppDispatchContext = createContext({});

function init() {
    return {
        events: new EventEmitter()
    }
}

function appReducer(state, action) {
    switch (action.type) {
        // case 'update': {
        //     state.token.setToken(action.token)
        //     return state
        // }
        default: {
            throw new Error(`Unhandled action type: ${action.type}`)
        }
    }
}

export function AppContextProvider({children}) {
    //const token = new Token(getStorage({ key: 'token' }))
    const [state, dispatch] = useReducer(appReducer, null, init)

    return (
        <AppContext.Provider value={state}>
            <AppDispatchContext.Provider value={dispatch}>
                {children}
            </AppDispatchContext.Provider>
        </AppContext.Provider>
    )
}
