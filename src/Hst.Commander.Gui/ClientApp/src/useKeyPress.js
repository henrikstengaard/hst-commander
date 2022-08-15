import { useEffect, useState } from "react";

const useKeyPress = (targetKey) => {
    let keyPressHoldTimer = null;
    
    const [state, setState] = useState({ keyPressed: null });

    useEffect(() => {
        keyPressHoldTimer = setTimeout(() => {
            //Action to be performed after holding down mouse
        }, 200); //Change 1000 to number of milliseconds required for mouse hold
        
        const downHandler = (event) => {
            // console.log('down',event.repeat);
            // if (event.key === targetKey) {
                setState({ keyPressed: event.key });
            // }
        };
        const upHandler = (event) => {
            // console.log('up',event.repeat);
            // if (event.repeat || event.key === targetKey) {
                setState({ keyPressed: null });
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