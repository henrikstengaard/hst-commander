import { useEffect, useState } from "react";

const useKeyPress = (targetKey) => {
    const [state, setState] = useState({ keyPressed: false });

    useEffect(() => {
        const downHandler = (event) => {
            // console.log('down',event.repeat);
            // if (event.key === targetKey) {
                setState({ keyPressed: event.key });
            // }
        };
        const upHandler = (event) => {
            // console.log('up',event.repeat);
            // if (event.repeat || event.key === targetKey) {
                setState({ keyPressed: event.key });
            // }
        };

        window.addEventListener("keydown", downHandler);
        window.addEventListener("keyup", upHandler);

        return () => {
            window.removeEventListener("keydown", downHandler);
            window.removeEventListener("keyup", upHandler);
        };
    }, [targetKey]);

    return state.keyPressed;
};

export default useKeyPress;