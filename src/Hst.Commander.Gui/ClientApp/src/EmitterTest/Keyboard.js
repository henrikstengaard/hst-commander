import { useEffect,useContext } from "react";
import {AppContext} from "./AppContext";

export default function Keyboard() {
    const {
        events
    } = useContext(AppContext)

    useEffect(() => {
        // const downHandler = (event) => {
        //     setState({ key: event.key, count: state.count++ });
        //     event.preventDefault();
        // };
        //
        const upHandler = (event) => {
            console.log('key')
            events.emit('KEYUP', event.key);
            event.preventDefault();
        };

        // window.addEventListener("keydown", downHandler);
        window.addEventListener("keyup", upHandler);

        return () => {
            // window.removeEventListener("keydown", downHandler);
            window.removeEventListener("keyup", upHandler);
        };
    });
}