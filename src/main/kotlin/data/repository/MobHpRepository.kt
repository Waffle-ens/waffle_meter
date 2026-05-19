package com.tbread.data.repository

class MobHpRepository {
    private val storage = HashMap<Int,Int>()

    fun get(key:Int):Int?{
        return storage[key]
    }

    fun set(key:Int, value:Int){
        storage[key] = value
    }

    fun flush(){
        storage.clear()
    }

}