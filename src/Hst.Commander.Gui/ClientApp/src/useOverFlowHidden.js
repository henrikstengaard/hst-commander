import { useEffect } from 'react'

const useOverFlowHidden = () => {
    useEffect(() => {
        document.body.style.overflow = 'hidden'

        return () => {
            document.body.style.overflow = 'auto' // cleanup or run on page unmount
        }
    }, [])
}

export default useOverFlowHidden