add_executable(serial
    serial.c
)

# Add pico_stdlib library which aggregates commonly used features
target_link_libraries(serial  pico_stdlib hardware_uart)

pico_add_extra_outputs(serial)
