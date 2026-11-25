# ps5-baglanti: Altyazı+'ın PS5 bağlantı yardımcısı (ayrı program, AGPL-3.0; bkz. LICENSE).
# chiaki-ng'nin kütüphanesi (third-party/chiaki-ng, git alt modülü) ve FFmpeg'in H.264 çözücüsüyle derlenir.
#
#   cmake -S tools/ps5-baglanti -B build/ps5 -G Ninja -DCMAKE_BUILD_TYPE=Release [-DFFMPEG_ROOT=<statik FFmpeg>]
#   cmake --build build/ps5
cmake_minimum_required(VERSION 3.20)
project(ps5_baglanti C)

set(CHIAKI_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../third-party/chiaki-ng" CACHE PATH "chiaki-ng kaynakları")
set(FFMPEG_ROOT "" CACHE PATH "FFmpeg kurulumu (include/, lib/); boşsa sistemdeki")

if(WIN32)
	# Tek başına çalışsın: MSYS2 DLL'lerine bağlı kalmasın.
	set(CMAKE_FIND_LIBRARY_SUFFIXES .a)
	set(OPENSSL_USE_STATIC_LIBS ON CACHE BOOL "" FORCE)
	add_compile_definitions(MINIUPNP_STATICLIB)
endif()

# Yalnız kütüphane: arayüz, komut satırı, testler, ses ve oyun kolu desteği kapalı.
foreach(opt CHIAKI_ENABLE_GUI CHIAKI_ENABLE_CLI CHIAKI_ENABLE_TESTS CHIAKI_ENABLE_FFMPEG_DECODER
		CHIAKI_ENABLE_PI_DECODER CHIAKI_LIB_ENABLE_OPUS CHIAKI_ENABLE_SPEEX CHIAKI_ENABLE_SETSU
		CHIAKI_ENABLE_STEAMDECK_NATIVE CHIAKI_ENABLE_STEAM_SHORTCUT CHIAKI_ENABLE_BOREALIS BUILD_SHARED_LIBS
		USE_NGHTTP2 USE_LIBIDN2 CURL_ZSTD CURL_BROTLI CURL_USE_LIBPSL CURL_USE_LIBSSH2 USE_LIBRTMP CURL_ZLIB)
	set(${opt} OFF CACHE STRING "" FORCE)
endforeach()
set(CURL_DISABLE_LDAP ON CACHE BOOL "" FORCE)
add_subdirectory("${CHIAKI_DIR}" chiaki EXCLUDE_FROM_ALL)

set(FF_LIBS)
foreach(lib avcodec swscale avutil)
	find_library(FF_${lib} NAMES ${lib} HINTS "${FFMPEG_ROOT}/lib" NO_DEFAULT_PATH)
	if(NOT FF_${lib})
		find_library(FF_${lib} NAMES ${lib} REQUIRED)
	endif()
	list(APPEND FF_LIBS ${FF_${lib}})
endforeach()
find_path(FF_INCLUDE libavcodec/avcodec.h HINTS "${FFMPEG_ROOT}/include")

add_executable(ps5-baglanti main.c)
target_include_directories(ps5-baglanti PRIVATE "${FF_INCLUDE}")
target_link_libraries(ps5-baglanti PRIVATE chiaki-lib ${FF_LIBS})
if(WIN32)
	target_link_libraries(ps5-baglanti PRIVATE ws2_32 iphlpapi bcrypt crypt32 secur32 advapi32 user32 shell32 ole32 normaliz)
	target_link_options(ps5-baglanti PRIVATE -static -static-libgcc -s)
else()
	target_link_libraries(ps5-baglanti PRIVATE m)
endif()
