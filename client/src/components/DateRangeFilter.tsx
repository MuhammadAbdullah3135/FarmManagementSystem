import React from 'react';
import { DatePicker, Space, Button } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import dayjs, { Dayjs } from 'dayjs';

interface DateRangeFilterProps {
  onChange: (from?: string, to?: string) => void;
  defaultValue?: [Dayjs | null, Dayjs | null];
}

const DateRangeFilter: React.FC<DateRangeFilterProps> = ({ onChange, defaultValue }) => {
  const [range, setRange] = React.useState<[Dayjs | null, Dayjs | null]>(
    defaultValue || [dayjs().subtract(30, 'day'), dayjs()]
  );

  const handleChange = (dates: [Dayjs | null, Dayjs | null] | null) => {
    if (dates && dates[0] && dates[1]) {
      setRange(dates);
      onChange(dates[0].toISOString(), dates[1].toISOString());
    } else {
      setRange([null, null]);
      onChange(undefined, undefined);
    }
  };

  const handleReset = () => {
    setRange([dayjs().subtract(30, 'day'), dayjs()]);
    onChange(dayjs().subtract(30, 'day').toISOString(), dayjs().toISOString());
  };

  return (
    <Space>
      <DatePicker.RangePicker
        value={range as [Dayjs, Dayjs]}
        onChange={(dates) => handleChange(dates as [Dayjs | null, Dayjs | null] | null)}
        allowClear
        placeholder={['From', 'To']}
      />
      <Button icon={<ReloadOutlined />} onClick={handleReset}>
        Reset
      </Button>
    </Space>
  );
};

export default DateRangeFilter;
