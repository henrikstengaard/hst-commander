import { useEffect, useState } from "react";

export default function KeyPressHandler() {
    const [state, setState] = useState({ key: null, count: 0 });

    useEffect(() => {
        // const downHandler = (event) => {
        //     setState({ key: event.key, count: state.count++ });
        //     event.preventDefault();
        // };
        //
        const upHandler = (event) => {
            setState({ key: event.key, count: 0 });
            event.preventDefault();
        };

        // window.addEventListener("keydown", downHandler);
        window.addEventListener("keyup", upHandler);

        return () => {
            // window.removeEventListener("keydown", downHandler);
            window.removeEventListener("keyup", upHandler);
        };
    });
    
    return state;
}